using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace arcunpack
{
    class Program
    {
        static void Main(string[] args)
        {            
            Console.WriteLine("arc utils, v0.3");
            if(args.Length == 0)
            {
                Console.WriteLine("extract: {0} <x> <filename>", System.AppDomain.CurrentDomain.FriendlyName);
                Console.WriteLine("pack: {0} <p> <filename> <dir to pack>", System.AppDomain.CurrentDomain.FriendlyName);
            }
            else if (args[0] == "x")
            {
                //Parse header
                //byte[] filedata = File.ReadAllBytes(args[0]);
                BinaryReader b = new BinaryReader(File.Open(args[1], FileMode.Open));
                byte[] header = b.ReadBytes(4);
                if (header[0] != 0x20 || header[1] != 0x11 || header[2] != 0x75 || header[3] != 0x19)
                {
                    Console.WriteLine("Invalid header - not arc file");
                    return;
                }
                int unkFlag1 = b.ReadInt32();
                int numFiles = b.ReadInt32();
                int unkFlag2 = b.ReadInt32();

                Console.WriteLine("UnkFlag1: " + unkFlag1);
                Console.WriteLine("Number of Files: " + numFiles);
                Console.WriteLine("UnkFlag2: " + unkFlag2);

                for (int i = 0; i < numFiles; i++)
                {
                    int fileStrOffset = b.ReadInt32();
                    int fileDataOffset = b.ReadInt32();
                    int fileDataDecomSize = b.ReadInt32();
                    int fileDataLen = b.ReadInt32();

                    long curPos = b.BaseStream.Position;

                    b.BaseStream.Position = fileStrOffset;
                    string fileName = ReadAscii(b);
                    Console.WriteLine("**************");
                    Console.WriteLine("File Name: " + fileName);
                    Console.WriteLine("File Data Decompressed Size: " + fileDataDecomSize);
                    Console.WriteLine("File Data Raw Offset: " + fileDataOffset);
                    Console.WriteLine("File Data Raw Length: " + fileDataLen);                    

                    bool isCompressed = false;

                    if (fileDataLen != fileDataDecomSize)
                    {
                        isCompressed = true;
                    }

                    b.BaseStream.Position = fileDataOffset;

                    byte[] fileBytes;
                    byte[] fileCompressedBytes = b.ReadBytes(fileDataLen);

                    if (isCompressed)
                    {
                        LZDecompress lz = new LZDecompress(fileCompressedBytes);
                        
                        fileBytes = lz.decompress();
                    }
                    else
                        fileBytes = fileCompressedBytes;

                    FileInfo file = new FileInfo(fileName);
                    file.Directory.Create();

                    File.WriteAllBytes(fileName, fileBytes);


                    b.BaseStream.Position = curPos;
                }
            }
            else if(args[0]=="p")
            {
                string pathToPack = args[2];
                string arcFileName = args[1];
                bool compression = true;
                if (args.Contains("--no-compression"))
                {
                    compression = false;
                }
                


                
                Console.WriteLine("Processing: " + pathToPack+" into "+arcFileName);

                DirectoryInfo rootdir = new DirectoryInfo(pathToPack);

                WalkDirectory(rootdir, arcFileName, pathToPack, compression);



                //Write header
                //Write unk 1
                //Write num files
                //Write unk 2
                //Write file offsets (file str offset, 
                //Write strings
                //Write file data
                FileStream output = new FileStream(arcFileName, FileMode.Create);
                output.WriteByte(0x20); //header
                output.WriteByte(0x11);
                output.WriteByte(0x75);
                output.WriteByte(0x19);
                output.Write(BitConverter.GetBytes(1), 0, 4); //unk
                output.Write(BitConverter.GetBytes(filesToPack), 0, 4);
                output.Write(BitConverter.GetBytes(2), 0, 4); //unk

                //table of contents - write blanks, we'll fill this in later

                long[] fStructOffsets = new long[filesToPack];

                for (int i = 0; i < filesToPack; i++)
               {
                    fStructOffsets[i] = output.Position;
                    output.Write(BitConverter.GetBytes(0), 0, 4); //String offset
                    output.Write(BitConverter.GetBytes(0), 0, 4); //Data offset
                    output.Write(BitConverter.GetBytes(decFileSizes[i]), 0, 4); //Uncompressed size
                    output.Write(BitConverter.GetBytes(compFileSizes[i]), 0, 4); //Compressed size
                }

                long[] fNameOffsets = new long[filesToPack];

                

                //file names
                for (int i = 0; i < filesToPack; i++)
                {
                    fNameOffsets[i] = output.Position;
                    fileNames[i] = fileNames[i].Replace('\\', '/');
                    byte[] toBytes = Encoding.ASCII.GetBytes(fileNames[i]+ "\0");
                    
                    output.Write(toBytes, 0, toBytes.Length);
                }

                long[] fDataOffsets = new long[filesToPack];

                //file data
                for(int i=0;i<filesToPack;i++)
                {
                    fDataOffsets[i] = output.Position;                    
                    byte[] filebytes = fileData[i].ToArray();
                    output.Write(filebytes, 0, filebytes.Length);
                }

                //go back and write file offsets
                for(int i=0;i<filesToPack;i++)
                {
                    output.Position = fStructOffsets[i];
                    output.Write(BitConverter.GetBytes(fNameOffsets[i]), 0, 4); //Uncompressed size
                    output.Write(BitConverter.GetBytes(fDataOffsets[i]), 0, 4); //Compressed size
                    output.Write(BitConverter.GetBytes(decFileSizes[i]), 0, 4); //Uncompressed size
                    output.Write(BitConverter.GetBytes(compFileSizes[i]), 0, 4); //Compressed size
                }
            }
        }

        public static int filesToPack = 0;
        static string[]  fileNames = new string[1000];
        static long[] decFileSizes = new long[1000];
        static long[] compFileSizes = new long[1000];
        static MemoryStream[] fileData = new MemoryStream[1000];

        static void WalkDirectory(DirectoryInfo root, string arcFileName, string pathToPack, bool compression)
        {
            FileInfo[] files = null;
            DirectoryInfo[] subDirs = null;


            files = root.GetFiles("*.*");

            if (files != null)
            {
                foreach (FileInfo file in files)
                {
                    string packFileStr = file.FullName.Substring(file.FullName.IndexOf(pathToPack));
                    

                    Console.WriteLine("{0} Packing: " + packFileStr + " into " + arcFileName, filesToPack);
                    fileNames[filesToPack] = packFileStr;
                    decFileSizes[filesToPack] = file.Length;

                    byte[] dbytes = File.ReadAllBytes(file.FullName);

                    if (dbytes.Length > 1000 && compression == true)
                    {
                        Console.WriteLine("Using compression");
                        LZCompress lz = new LZCompress();
                        lz.write(dbytes);
                        lz.close();
                        fileData[filesToPack] = (MemoryStream)lz.outputdata();
                    }
                    else
                    {
                        Console.WriteLine("Not using compression");
                        fileData[filesToPack] = new MemoryStream(dbytes);
                    }

                    compFileSizes[filesToPack] = fileData[filesToPack].Length;

                    filesToPack++;

                }

                subDirs = root.GetDirectories();
                foreach (DirectoryInfo dir in subDirs)
                {
                    WalkDirectory(dir, arcFileName, pathToPack, compression);
                }

            }
        }

        static string ReadAscii(BinaryReader input)
        {
            List<byte> strBytes = new List<byte>();
            int b;
            while ((b = input.ReadByte()) != 0x00)
                strBytes.Add((byte)b);
            return Encoding.ASCII.GetString(strBytes.ToArray());
        }
    }
}
