using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NRF24
{
    class Program
    {
        private const ushort KEEP = 4;
        private const int BAUDRATE = 2000000;
        private static Device watch = null;
        private static SerialPort serial = null;
        private static bool IsTuning = false;
        class Device
        {
            private FileStream _file = null;
            private BinaryWriter _writer = null;
            private BinaryReader _reader = null;
            public ulong Address { get; set; }
            public ushort Channel { get; set; }
            public string DataRate { get; set; }
            public uint Seen { get; set; }
            public BinaryWriter FileStreamOut
            {
                get
                {
                    if (_file == null || _writer == null)
                    {
                        lock (this)
                        {
                            _file = _file == null ? File.Open(string.Format("{0:X}_{1}.bin", Address, Channel), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read) : _file;
                            _writer = new BinaryWriter(_file);
                        }
                    }

                    return _writer;
                }
            }
            public BinaryReader FileStreamIn
            {
                get
                {
                    if (_file == null || _reader == null)
                    {
                        lock (this)
                        {
                            _file = _file == null ? File.Open(string.Format("{0:X}_{1}.bin", Address, Channel), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read) : _file;
                            _reader = new BinaryReader(_file);
                        }
                    }

                    return _reader;
                }
            }
        }

        private static void ResetArduino()
        {
            if (serial != null)
            {
                serial.ReadExisting();
                Console.WriteLine("Resetting arduino...");
                do
                {
                    while (serial.BytesToRead == 0)
                    {
                        serial.DtrEnable = true;
                        serial.Close();
                        //Thread.Sleep(500);
                        serial.Open();
                        Thread.Sleep(500);
                    }
                }
                while (serial.ReadExisting() != "255");
                serial.DtrEnable = false;
                serial.Close();
                serial.Open();
                Thread.Sleep(500);

            }
        }
        private static void Go()
        {
            if (serial != null)
            {
                do
                {
                    serial.WriteLine("!");
                    Thread.Sleep(1000);
                } while (serial.BytesToRead == 0 || serial.ReadChar() != '!');
                serial.ReadExisting();
            }
        }
        private static void TuneIn()
        {
            string junk = "";
            IsTuning = true;
            Console.Clear();
            ResetArduino();
            if (watch != null)
            {
                if (Convert.ToString(BitConverter.GetBytes(watch.Address).First(), 2).StartsWith("0"))
                {
                    do
                    {
                        while (serial.BytesToRead == 0)
                        {
                            serial.WriteLine("5");
                            serial.BaseStream.Flush();
                            Thread.Sleep(500);
                        }
                        junk = serial.ReadExisting();
                    } while (junk.Trim() != "0x55");
                }
                junk = "";
                if (watch.Channel > 0)
                {
                    do
                    {
                        while (serial.BytesToRead == 0)
                        {
                            serial.WriteLine("c" + watch.Channel);
                            serial.BaseStream.Flush();
                            Thread.Sleep(500);
                        }
                        junk = serial.ReadExisting();
                    } while (junk.Trim() != watch.Channel.ToString());
                }
            }
            Go();
            IsTuning = false;
        }
        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }
        public static bool ShiftLeft(byte[] bytes)
        {
            bool leftMostCarryFlag = false;

            // Iterate through the elements of the array from left to right.
            for (int index = 0; index < bytes.Length; index++)
            {
                // If the leftmost bit of the current byte is 1 then we have a carry.
                bool carryFlag = (bytes[index] & 0x80) > 0;

                if (index > 0)
                {
                    if (carryFlag == true)
                    {
                        // Apply the carry to the rightmost bit of the current bytes neighbor to the left.
                        bytes[index - 1] = (byte)(bytes[index - 1] | 0x01);
                    }
                }
                else
                {
                    leftMostCarryFlag = carryFlag;
                }

                bytes[index] = (byte)(bytes[index] << 1);
            }

            return leftMostCarryFlag;
        }

        static void Main(string[] args)
        {
            ushort chan = 0;
            ulong reads = 0;
            var addresses = new ConcurrentDictionary<ulong, Device>();
            var com = SerialPort.GetPortNames().FirstOrDefault();
            serial = new SerialPort(com, BAUDRATE, Parity.None, 8, StopBits.One);
            serial.DtrEnable = true;
            serial.Open();
            serial.DtrEnable = false;
            Console.WriteLine("Connecting to arduino...");

            ResetArduino();
            Go();

            Console.WriteLine("Starting scan...");

            var listen = Task.Run(() =>
            {
                while (true)
                {
                    while (serial.IsOpen)
                    {
                        if (IsTuning) continue;
                        string line = "";
                        try
                        {
                            line = serial.ReadLine();
                        }
                        catch { }
                        var split = line.Split(',');
                        if (split.Length == 8 && split[0] == "data" && split[7] == "atad")
                        {
                            try
                            {
                                var hexAddress = split[5].Substring(2, 10);
                                var address = Convert.ToUInt64(hexAddress, 16);
                                chan = ushort.Parse(split[3]);
                                addresses.AddOrUpdate(address, new Device()
                                {
                                    Address = address,
                                    Channel = chan,
                                    DataRate = split[4],
                                    Seen = 1
                                },
                                (x, y) =>
                                {
                                    y.Seen++;
                                    return y;
                                });
                                if (watch != null && address == watch.Address)
                                {
                                    var x = split[6].Replace("0x", "").TrimEnd('0');
                                    x = x + (x.Length % 2 == 0 ? "" : "0");
                                    if (x.Length > 0)
                                    {
                                        byte[] data = null;

                                        if (x.StartsWith("C") && x.EndsWith("80")) //Enhanced mode we need to get rid of the first 9 bits...
                                        {
                                            data = StringToByteArray(x);
                                            ShiftLeft(data);
                                        }
                                        else
                                            data = StringToByteArray(x);

                                        watch.FileStreamOut.Write(data.Skip(1).Take(data.Length - 2).ToArray());
                                        watch.FileStreamOut.Flush();
                                    }

                                }
                            }
                            catch { }
                        }

                        reads++;
                    }
                }
            });

            var change = Task.Run(() =>
            {
                const uint channels = 128;
                ulong tick = 0;
                var addr = false;
                var rate = 0;
                while (true)
                {
                    Thread.Sleep(400);
                    if (watch == null)
                    {
                        tick++;
                        serial.WriteLine("+");
                        serial.BaseStream.Flush();
                        if (tick % channels == 0)
                        {
                            if (addr)
                                serial.WriteLine("a");
                            else
                                serial.WriteLine("5");

                            serial.BaseStream.Flush();
                            addr = !addr;
                        }
                    }
                }
            });

            var process = Task.Run(() =>
            {
                while (true)
                {
                    Thread.Sleep(1000);
                    var removable = addresses.Where(x => x.Value.Seen < KEEP).ToList();
                    removable.AsParallel().ForAll(x =>
                    {
                        Device o;
                        addresses.TryRemove(x.Key, out o);
                    });

                    var ordered = addresses.Where(x => x.Value.Seen > KEEP).OrderByDescending(x => x.Value.Seen).ToList();

                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(false);
                        int id = 0;
                        if (key.Key == ConsoleKey.Escape)
                            watch = null;
                        else if (int.TryParse(Convert.ToString(key.KeyChar), out id))
                        {
                            watch = ordered.Skip(id).Take(1).Select(x => x.Value).FirstOrDefault();
                            TuneIn();
                        }
                    }

                    Console.Clear();
                    if (watch == null)
                    {
                        int i = 0;
                        Console.WriteLine("ID:\tAddress:\tChannel:\tRate:\tCount:");
                        Console.WriteLine("");
                        foreach (var a in ordered)
                        {
                            Console.WriteLine("{0}\t0x{1:X}\t{2}\t\t{3}\t{4}", i++, a.Value.Address, a.Value.Channel, a.Value.DataRate, a.Value.Seen);
                        }
                        Console.WriteLine("");
                        Console.Write("Enter ID: ");
                    }
                    else
                    {
                        if (!IsTuning)
                        {
                            Console.WriteLine("Watching 0x{0:X} on channel {1}", watch.Address, watch.Channel);
                            Console.WriteLine("Output can be found in {0:X}_{1}.bin", watch.Address, watch.Channel);
                        }
                        else
                        {
                            Console.WriteLine("Tuning to 0x{0:X} on channel {1}", watch.Address, watch.Channel);
                        }
                        //var data = watch.Data.Skip(watch.Data.Count - 20).ToArray();
                        //foreach (var d in data)
                        //{
                        //    Console.WriteLine("0x" + BitConverter.ToString(d).Replace("-",""));
                        //    foreach (var b in d)
                        //    {
                        //        Console.Write(Convert.ToString(b, 2).PadLeft(8, '0') + " ");
                        //    }
                        //    Console.Write(Environment.NewLine);
                        //    Console.WriteLine(Encoding.ASCII.GetString(d));

                        //    /*
                        //    var x = d.Replace("0x", "").TrimEnd('0');
                        //    x = x + (x.Length % 2 == 0 ? "" : "0");
                        //    //x = new string(x.Reverse().ToArray());
                        //    if (x.Length > 0)
                        //    {
                        //        Console.WriteLine(x);
                        //        for (int i = 0; i < x.Length; i += 2)
                        //        {
                        //            Console.Write(Convert.ToString(Convert.ToUInt32(x.Substring(i, 2), 16), 2).PadLeft(8, '0') + " ");
                        //            //Console.Write(Convert.ToChar(Convert.ToUInt32(x.Substring(i, 2), 16)) + " ");
                        //        }

                        //        Console.WriteLine();
                        //    }
                        //    */
                        //}

                        //Console.Write(new string(watch.Data.Skip(Math.Max(0, watch.Data.Count - 500)).Select(x => Convert.ToChar(x)).ToArray()));
                    }
                }

            });

            Task.WaitAll(listen, change, process);
        }
    }
}
