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
   public class Bootloader
    {
        private byte[] UsbWriteBuffer;
        private byte[] UsbReadBuffer;
        private HIDAPIInterface.USBHIDDevice usbdev = null;

        private HIDAPIInterface.DeviceScanner usbdevScan = null;

        public HIDAPIInterface.USBHIDDevice Usbdev
        {
            get { return usbdev; }
        }

        private QueryResponse queryRes = new QueryResponse();
        private ExtendedQueryResponse exQueryRes = new ExtendedQueryResponse();
        private IntelHex.IntelHexFile write_hexfile;
        private byte[] readbuf = null;

        public byte[] ReadBuffer
        {
            get { return readbuf; }
        }

        public bool Connected { get => connected;  }

        private Boolean connected = false;
        public bool verbose = false;
        private UInt16 VID = 0x04D8;
        private UInt16 PID = 0x003C;
        public Bootloader()
        {
            UsbWriteBuffer = new byte[64];
            UsbReadBuffer = new byte[64];
            usbdevScan = new HIDAPIInterface.DeviceScanner(VID, PID, 200);
            Connect();

        }
        public Bootloader(UInt16 vid, UInt16 pid)
        {
            UsbWriteBuffer = new byte[64];
            UsbReadBuffer = new byte[64];
            VID = vid;
            PID = pid;
            usbdevScan = new HIDAPIInterface.DeviceScanner(VID, PID, 200);
            Connect();
        }

        public bool ExportReadBin(string outpath)
        {
            bool ret = false;
            FileStream fs = null;
            BinaryWriter bw = null;
            if (readbuf == null)
            {
                Console.WriteLine("Nothing read. No binary export.");
                return false;
            }
            Console.WriteLine("Export flash content...");
            try
            {
                fs = new FileStream(outpath, FileMode.Create, FileAccess.Write);
                bw = new BinaryWriter(fs);
                bw.Write(readbuf, 0, readbuf.Length);
                Console.WriteLine("Flash content exported to {0}", outpath);
                ret = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                if (bw != null)
                    bw.Close();
                if (fs != null)
                    fs.Close();
            }
            return ret;
        }

        public bool ReadFlash(string outpath)
        {
            bool ret = ReadFlash();
            ret = ExportReadBin(outpath);
            return ret;
        }

        public byte[] ReadData(UInt32 address, int count)
        {
            if (!usbdev.IsOpen || !connected)
                return null;

            byte[] readbuf = new byte[count];
            for (int i = 0; i < readbuf.Length; i++)
                readbuf[i] = 0xff;
            GenericPacket tx_pkt = new GenericPacket();
            GenericPacket rx_pkt = new GenericPacket();

            tx_pkt.Command = BootCommands.GET_DATA;

            tx_pkt.Address = address;
            tx_pkt.Size = (byte)count;
            Array.Clear(UsbWriteBuffer, 0, UsbReadBuffer.Length);
            Console.Write("Reading {0} bytes from address 0x{1:X5}: ", tx_pkt.Size, address);
            UsbWriteBuffer = tx_pkt.Packet;
            int writtenbytes = usbdev.Write(UsbWriteBuffer);
            if (writtenbytes > 0)
            {
                Array.Clear(UsbReadBuffer, 0, UsbReadBuffer.Length);
                int readbyte = usbdev.Read(UsbReadBuffer, 200);
                if (readbyte >= 64)
                {

                    rx_pkt.Packet = UsbReadBuffer;
                    if (verbose)
                        Console.WriteLine("OK. {0} bytes read.", rx_pkt.Size);
                    Array.Copy(rx_pkt.Data, 0, readbuf, 0, rx_pkt.Size);

                    address += rx_pkt.Size;
                }

            }
            else
            {
                Console.WriteLine("Error while reading {0}. USB write error.", address);
               
            }
            return readbuf;

        }
        public bool ReadFlash()
        {
            bool ret=false;
            if (!usbdev.IsOpen || !connected)
                return false;
            readbuf = new byte[0x1FFFe];
            for (int i = 0; i < readbuf.Length; i++)
                readbuf[i] = 0xff;
            GenericPacket tx_pkt = new GenericPacket();
            GenericPacket rx_pkt = new GenericPacket();
            tx_pkt.Command = BootCommands.GET_DATA;
            UInt32 address = queryRes.ProgramMemStart;
            uint end_address=queryRes.ProgramMemStart + queryRes.ProgramMemLength;


            Console.WriteLine("\r\nStart Reading Flash!\r\n");
            long pkt = 0;
            while (address < end_address)
            {

                tx_pkt.Address = address;
                //if((end_address - address) > 58)
                    tx_pkt.Size = 58;
                //else
                //    tx_pkt.Size = (byte)(end_address - address);

                Array.Clear(UsbWriteBuffer, 0, UsbReadBuffer.Length);
                UsbWriteBuffer = tx_pkt.Packet;
                if(verbose)
                    Console.Write("Reading {0} bytes from address 0x{1:X5}: ",tx_pkt.Size, address);
                int writtenbytes = usbdev.Write(UsbWriteBuffer);
                if (writtenbytes > 0)
                {
                    Array.Clear(UsbReadBuffer, 0, UsbReadBuffer.Length);
                    int readbyte = usbdev.Read(UsbReadBuffer, 200);
                    if (readbyte >= 64)
                    {

                        rx_pkt.Packet = UsbReadBuffer;
                        if (verbose)
                            Console.WriteLine("OK. {0} bytes read.", rx_pkt.Size);
                        Array.Copy(rx_pkt.Data, 0, readbuf, address, rx_pkt.Size);

                        address += rx_pkt.Size;
                    }
                    pkt++;

                }
                else
                {
                    Console.WriteLine("Error while reading {0}. USB write error.", address);
                    return false;
                }

            }
            Console.Write("Successfully read from address 0x{0:X5} to  address 0x{1:X5}", queryRes.ProgramMemStart, address);

            return ret;
        }
        public bool WriteFlashFromBin(string binfile)
        {
            bool ret = false;
            FileStream fs = null;
            byte[] bindata = null;
            try
            {
                fs = new FileStream(binfile, FileMode.Open, FileAccess.Read);
                bindata = new byte[fs.Length];
                fs.Read(bindata, 0, (int)fs.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                if (fs != null)
                    fs.Close();
            }

            Console.WriteLine("Start writing flash with binfile {0}. Binary size: {1}", binfile, bindata.Length);
            ret = WriteFlash(bindata);

            return ret;

        }
        public bool WriteFlash(byte[] writedata)
        {
            if (!usbdev.IsOpen || !connected)
                return false;

            Console.WriteLine("Start writing flash with binary size: {0}", writedata.Length);
            //actual write
            double progress = 0.0;
            int writtentotalbytes = 0;
            UInt32 address = queryRes.ProgramMemStart;
            while (address < (queryRes.ProgramMemStart + queryRes.ProgramMemLength))
            {
                int writtenbytes = writeBlock(address, writedata);
                if (writtenbytes > 0)
                    address += (uint)writtenbytes;
                else
                {
                    Console.WriteLine("Error while writing {0}", address);
                    Console.WriteLine("Cancel and erasing device again");
                    EraseDevice();
                    return false;
                }
                writtentotalbytes += writtenbytes;
                progress = 100 * (double)writtentotalbytes / (double)writedata.Length;
                int progint = (int)(progress * 100);
                if (progint % 500 == 0 && progint > 0)
                    Console.WriteLine("Progess: {0:0.0}%", progress);
            }
            if (queryRes.bootloader_V101OrNewer)
            {
                Console.WriteLine("Bootloader with V101 or newer. Signing flash.");
                Array.Clear(UsbWriteBuffer, 0, UsbReadBuffer.Length);
                GenericPacket pkt = new GenericPacket();
                pkt.Command = BootCommands.SIGN_FLASH;
                UsbWriteBuffer = pkt.Packet;
                Console.WriteLine("SIGN_FLASH");
                int transferbytes = usbdev.Write(UsbWriteBuffer);

            }
            else
                Console.WriteLine("Old Bootloader. Flash content not signed.");
            return true;

        }

        public bool WriteFlashHexFile(string hexpath)
        {
            if (!usbdev.IsOpen || !connected)
                return false;
            write_hexfile = new IntelHex.IntelHexFile();
            FileStream fs = null;
            StreamReader sr = null;
            try
            {
                fs = new FileStream(hexpath, FileMode.Open, FileAccess.Read);
                sr = new StreamReader(fs);
                write_hexfile.LoadFromHexFile(sr);

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                if (sr != null)
                    sr.Close();
                if (fs != null)
                    fs.Close();
            }

            if (!write_hexfile.FileValid)
            {
                Console.WriteLine("File {0} contains errors. Abort.", hexpath);
                return false;
            }

            Console.WriteLine("Start writing flash with File {0}. Binary size: {1}", hexpath, write_hexfile.BinarySize);
            bool ret = WriteFlash(write_hexfile.Binary);
            return ret;

        }


        /// <summary>
        /// Writes one (USB HID) packet (max 58 bytes) to the flash block.
        /// </summary>
        /// <param name="address">Startaddress of the flashblock. Have to be an even address</param>
        /// <param name="data">Bytearray with the content to write. </param>
        /// <returns></returns>
        private int writeBlock(UInt32 address, byte[] data)
        {
            if (!usbdev.IsOpen || !connected)
                return -1;
            Array.Clear(UsbWriteBuffer, 0, UsbReadBuffer.Length);

            GenericPacket pkt = new GenericPacket();
            pkt.Command = BootCommands.PROGRAM_DEVICE;
            if ((address & 0x1) != 0)
            {
                Console.WriteLine("Error on write: address {0} not even", address);
                return -1;
            }
            pkt.Address = address;

            if ((data.Length - address) >= 58)
                pkt.Size = 58;
            else
            {
                pkt.Command = BootCommands.PROGRAM_COMPLETE;
                pkt.Size = (byte)(data.Length - address);
            }
            Array.Copy(data, address, pkt.Data, 0, pkt.Size);

            UsbWriteBuffer = pkt.Packet;
            if(verbose)
                Console.WriteLine("Write at address 0x{0}. Len {1}", address.ToString("X6"), pkt.Size);
            int writtenbytes = usbdev.Write(UsbWriteBuffer);
            if (writtenbytes >= 64)
                return pkt.Size;
            else
                return -1;


        }

        public bool ResetDevice()
        {
            if (!usbdev.IsOpen || !connected)
                return false;
            Array.Clear(UsbReadBuffer, 0, UsbReadBuffer.Length);
            Array.Clear(UsbWriteBuffer, 0, UsbReadBuffer.Length);

            UsbWriteBuffer[0] = (byte)BootCommands.RESET_DEVICE;
            Console.WriteLine("Send Reset Command");
            int writtenbytes = usbdev.Write(UsbWriteBuffer);
            Thread.Sleep(200);
            if (writtenbytes > 0)
                return true;
            else
                return false;
        }

        public bool EraseDevice()
        {
            if (!usbdev.IsOpen || !connected)
                return false;
            Array.Clear(UsbReadBuffer, 0, UsbReadBuffer.Length);
            Array.Clear(UsbWriteBuffer, 0, UsbReadBuffer.Length);
            UsbWriteBuffer[0] = (byte)BootCommands.ERASE_DEVICE;
            Console.WriteLine("Start Erase");
            int writtenbytes = usbdev.Write(UsbWriteBuffer);
            Thread.Sleep(200);
            Array.Clear(UsbWriteBuffer, 0, UsbReadBuffer.Length);

            bool queryOK = false;
            QueryTx();
            Stopwatch timeout = new Stopwatch();
            timeout.Start();
            do
            {
                queryOK = QueryRx(200);
                if (timeout.Elapsed.Seconds > 40)
                {
                    Console.WriteLine("Timeout on erase.");
                    return false;
                }
                Thread.Sleep(100);
                Console.Write(".");
            }
            while (!queryOK);
            Console.WriteLine("Erase completed");
            return true;
        }

        public bool QueryExtended()
        {
            if (!usbdev.IsOpen)
                return false;
            //extended query
            if (queryRes.bootloader_V101OrNewer)
            {
                Array.Clear(UsbReadBuffer, 0, UsbReadBuffer.Length);
                Array.Clear(UsbWriteBuffer, 0, UsbWriteBuffer.Length);
                UsbWriteBuffer[0] = (byte)BootCommands.QUERY_EXTENDED_INFO;
                int writtenbytes = usbdev.Write(UsbWriteBuffer);
                int readbyte = usbdev.Read(UsbReadBuffer, 1000);
                if (readbyte >= 64)
                {
                    exQueryRes.ParseArray(UsbReadBuffer);
                    exQueryRes.Print();
                    return true;
                }
                else
                    return false;
            }
            else
                return false;
        }
        public bool Query()
        {
            bool ret = false;
            ret = QueryTx();
            ret = QueryRx(200);
            if (ret)
                queryRes.Print();
            return ret;
        }
        private bool QueryRx(int timeout)
        {
            Array.Clear(UsbReadBuffer, 0, UsbReadBuffer.Length);
            int readbyte = usbdev.Read(UsbReadBuffer, timeout);
            if (readbyte >= 64)
            {
                queryRes.ParseArray(UsbReadBuffer);
                return true;
            }
            else
                return false;
        }
        private bool QueryTx()
        {
            Array.Clear(UsbWriteBuffer, 0, UsbWriteBuffer.Length);
            UsbWriteBuffer[0] = (byte)BootCommands.QUERY_DEVICE;
            int writtenbytes = usbdev.Write(UsbWriteBuffer);
            if (writtenbytes >= 64)
                return true;
            else
                return false;
        }
        public void Disconnect()
        {
            if (usbdev != null)
            {
                if (usbdev.IsOpen)
                {
                    usbdev.Dispose();
                }
            }
            connected = false;
        }


        public bool Connect()
        {
            
            if (!usbdevScan.ScanOnce())
            {
                connected = false;
                if (usbdev != null)
                    usbdev.Dispose();
                return false;
            }
            else
            {
                if (!connected)
                {
                    if (usbdev != null)
                        usbdev.Dispose();
                    usbdev = new HIDAPIInterface.USBHIDDevice(VID, PID, null, false, 64);
                    if (!usbdev.IsOpen)
                        return false;
                }
            }
            queryRes = new QueryResponse();
            exQueryRes = new ExtendedQueryResponse();
            bool con = Query();
            QueryExtended();
            if (queryRes.Command == (byte)BootCommands.QUERY_DEVICE)
                connected = true;
            else
                connected = false;
            return connected;
        }
    }

}