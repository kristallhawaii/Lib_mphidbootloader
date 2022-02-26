/*
 * SPDX-FileCopyrightText: © 2021 Matthias Keller <mkeller_service@gmx.de>
 *
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Lib_mphidflashsharp
{
    enum BootCommands : byte
    {
        QUERY_DEVICE = 0x02, //Command that the host uses to learn about the device (what regions can be programmed, and what type of memory is the region)
        UNLOCK_CONFIG = 0x03, //Note, this command is used for both locking and unlocking the config bits (see the "//Unlock Configs Command Definitions" below)
        ERASE_DEVICE = 0x04, //Host sends this command to start an erase operation. Firmware controls which pages should be erased.
        PROGRAM_DEVICE = 0x05, //If host is going to send a full RequestDataBlockSize to be programmed, it uses this command.
        PROGRAM_COMPLETE = 0x06, //If host send less than a RequestDataBlockSize to be programmed, or if it wished to program whatever was left in the buffer, it uses this command.
        GET_DATA = 0x07, //The host sends this command in order to read out memory from the device. Used during verify (and read/export hex operations)
        RESET_DEVICE = 0x08, //Resets the microcontroller, so it can update the config bits (if they were programmed, and so as to leave the bootloader (and potentially go back into the main application)
        SIGN_FLASH = 0x09, //The host PC application should send this command after the verify operation has completed successfully. If checksums are used instead of a true verify (due to ALLOW_GET_DATA_COMMAND being commented), then the host PC application should send SIGN_FLASH command after is has verified the checksums are as exected. The firmware will then program the SIGNATURE_WORD into flash at the SIGNATURE_ADDRESS.
        QUERY_EXTENDED_INFO = 0x0C //Used by host PC app to get additional info about the device, beyond the basic NVM layout provided by the query device command
    }

    enum BootResponseType : byte
    {
        //Query Device Response "Types" 
        MEMORY_REGION_PROGRAM_MEM = 0x01, //When the host sends a QUERY_DEVICE command, need to respond by populating a list of valid memory regions that exist in the device (and should be programmed)
        MEMORY_REGION_EEDATA = 0x02,
        MEMORY_REGION_CONFIG = 0x03,
        MEMORY_REGION_USERID = 0x04,
        MEMORY_REGION_END = 0xFF, //Sort of serves as a "null terminator" like number, which denotes the end of the memory region list has been reached.
        BOOTLOADER_V1_01_OR_NEWER_FLAG = 0xA5 //Tacked on in the VersionFlag byte, to indicate when using newer version of bootloader with extended query info available
    }

    class GenericPacket
    {
        public BootCommands Command;
        public UInt32 Address;
        public byte Size;
        public byte[] Data;
        public GenericPacket()
        {
            usbPacket = new byte[64];
            Data = new byte[58];
        }

        private byte[] usbPacket;
        public byte[] Packet
        {
            get
            {
                usbPacket[0] = (byte)Command;
                usbPacket[1] = (byte)(Address & 0x000000FFU);
                usbPacket[2] = (byte)((Address & 0x0000FF00U) >> 8);
                usbPacket[3] = (byte)((Address & 0x00FF0000U) >> 16);
                usbPacket[4] = (byte)((Address & 0xFF000000U) >> 24);
                usbPacket[5] = Size;
                Array.Copy(Data, 0, usbPacket, 6, 58);
                return usbPacket;
            }
            set
            {
                usbPacket = value;
                Command = (BootCommands)usbPacket[0];
                Address = BitConverter.ToUInt32(usbPacket, 1);
                Size = usbPacket[5];
                Array.Copy(usbPacket, 6, Data, 0, 58);
            }
        }
    }

    class ExtendedQueryResponse
    {
        public byte Command;
        public UInt16 BootloaderVersion;
        public UInt16 ApplicationVersion;
        public UInt32 SignatureAddress;
        public UInt16 SignatureValue;
        public UInt32 ErasePageSize;
        public byte Config1LMask;
        public byte Config1HMask;
        public byte Config2LMask;
        public byte Config2HMask;
        public byte Config3LMask;
        public byte Config3HMask;
        public byte Config4LMask;
        public byte Config4HMask;
        public byte Config5LMask;
        public byte Config5HMask;
        public byte Config6LMask;
        public byte Config6HMask;
        public byte Config7LMask;
        public byte Config7HMask;
        public void Print()
        {
            Console.WriteLine("BootloaderVersion:  0x{0} V{1}.{2}", BootloaderVersion.ToString("X4"), (BootloaderVersion >> 8).ToString(), (BootloaderVersion & 0xff).ToString());
            Console.WriteLine("ApplicationVersion: 0x{0} V{1}.{2}", ApplicationVersion.ToString("X4"), (ApplicationVersion >> 8).ToString(), (ApplicationVersion & 0xff).ToString());
            Console.WriteLine("SignatureAddress:   0x{0}", SignatureAddress.ToString("X8"));
            Console.WriteLine("SignatureValue:     0x{0}", SignatureValue.ToString("X4"));
        }
        public void ParseArray(byte[] buf)
        {
            if (buf.Length < 64)
                return;
            int i = 0;
            Command = buf[i++];
            BootloaderVersion = BitConverter.ToUInt16(buf, i); i += 2;
            ApplicationVersion = BitConverter.ToUInt16(buf, i); i += 2;
            SignatureAddress = BitConverter.ToUInt32(buf, i); i += 4;
            SignatureValue = BitConverter.ToUInt16(buf, i); i += 2;
            ErasePageSize = BitConverter.ToUInt32(buf, i); i += 4;
            Config1LMask = buf[i++];
            Config1HMask = buf[i++];
            Config2LMask = buf[i++];
            Config2HMask = buf[i++];
            Config3LMask = buf[i++];
            Config3HMask = buf[i++];
            Config4LMask = buf[i++];
            Config4HMask = buf[i++];
            Config5LMask = buf[i++];
            Config5HMask = buf[i++];
            Config6LMask = buf[i++];
            Config6HMask = buf[i++];
            Config7LMask = buf[i++];
            Config7HMask = buf[i++];

        }

    }
    class QueryResponse
    {
        public byte Command;
        public byte PacketDataFieldSize;
        public byte BytesPerAddress;
        public UInt32 ProgramMemStart;
        public UInt32 ProgramMemLength;
        public byte Type1;
        public UInt32 Address1;
        public UInt32 Length1;
        public byte Type2;
        public UInt32 Address2;
        public UInt32 Length2;
        public byte Type3;
        public UInt32 Address3;
        public UInt32 Length3;
        public byte Type4;
        public UInt32 Address4;
        public UInt32 Length4;
        public byte Type5;
        public UInt32 Address5;
        public UInt32 Length5;
        public byte Type6;
        public UInt32 Address6;
        public UInt32 Length6;
        public byte VersionFlag;
        public byte[] ExtraPadBytes;
        public bool bootloader_V101OrNewer = false;

        public void Print(bool verbose = false)
        {
            if (verbose)
            {
                Console.WriteLine("PacketDataFieldSize:                   0x{0}", PacketDataFieldSize.ToString("X2"));
                Console.WriteLine("BytesPerAddress:                       0x{0}", BytesPerAddress.ToString("X2"));
                Console.WriteLine("Type1 (MEMORY_REGION_PROGRAM_MEM):     0x{0}", Type1.ToString("X2"));
                Console.WriteLine("Address1 (PROGRAM_MEM_START_ADDRESS):  0x{0}", Address1.ToString("X8"));
                Console.WriteLine("Length1 (Size of program memory area): 0x{0}", Length1.ToString("X8"));
                Console.WriteLine("Type2 (MEMORY_REGION_CONFIG):          0x{0}", Type2.ToString("X2"));
                Console.WriteLine("Address2 (CONFIG_WORDS_START_ADDRESS): 0x{0}", Address2.ToString("X8"));
                Console.WriteLine("Length2 (CONFIG_WORDS_SECTION_LENGTH): 0x{0}", Length2.ToString("X8"));
                Console.WriteLine("Type3 (MEMORY_REGION_END):             0x{0}", Type2.ToString("X2"));
            }
            if (bootloader_V101OrNewer)
                Console.WriteLine("Bootloader Version V1.1 or newer");
            else
                Console.WriteLine("Bootloader old Version V1.0");
        }
        public void ParseArray(byte[] buf)
        {
            if (buf.Length < 64)
                return;
            int i = 0;

            Command = buf[i++];
            PacketDataFieldSize = buf[i++];
            BytesPerAddress = buf[i++];
            Type1 = buf[i++];
            Address1 = BitConverter.ToUInt32(buf, i); i += 4;
            ProgramMemStart = Address1;
            Length1 = BitConverter.ToUInt32(buf, i); i += 4;
            ProgramMemLength = Length1;

            Type2 = buf[i++];
            Address2 = BitConverter.ToUInt32(buf, i); i += 4;
            Length2 = BitConverter.ToUInt32(buf, i); i += 4;
            Type3 = buf[i++];
            Address3 = BitConverter.ToUInt32(buf, i); i += 4;
            Length3 = BitConverter.ToUInt32(buf, i); i += 4;
            Type4 = buf[i++];
            Address4 = BitConverter.ToUInt32(buf, i); i += 4;
            Length4 = BitConverter.ToUInt32(buf, i); i += 4;
            Type5 = buf[i++];
            Address5 = BitConverter.ToUInt32(buf, i); i += 4;
            Length5 = BitConverter.ToUInt32(buf, i); i += 4;
            Type6 = buf[i++];
            Address6 = BitConverter.ToUInt32(buf, i); i += 4;
            Length6 = BitConverter.ToUInt32(buf, i); i += 4;

            VersionFlag = buf[i++];
            if (VersionFlag == 0xA5)
                bootloader_V101OrNewer = true;
            else
                bootloader_V101OrNewer = false;
            ExtraPadBytes = new byte[6];
            Array.Copy(buf, i, ExtraPadBytes, 0, 6);
        }
    }


}