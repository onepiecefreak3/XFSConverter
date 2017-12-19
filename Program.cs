using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.Xml;
using System.Runtime.InteropServices;
using XFSConverter.IO;
using System.IO;
using System.Globalization;
using System.Drawing;

namespace XFSConverter
{
    public class Program
    {
        static List<int> structOffs = new List<int>();

        static BinaryReaderX structBr;
        static BinaryReaderX paramBr;

        #region XML
        public static List<Structure> tree = new List<Structure>();

        [XmlRoot("XFS")]
        public class Structure
        {
            [XmlElement("data")]
            public List<Data> data = null;
        }

        public class Data
        {
            [XmlAttribute]
            public string name;
            [XmlAttribute]
            public ushort type;
            [XmlAttribute]
            public short dataLength;

            [XmlElement("XFS_structure")]
            public List<Structure> structure = null;
            [XmlElement("TypePath")]
            public List<TypePath> typePath = null;
            [XmlElement("Value")]
            public List<string> Value = null;
            [XmlElement("Vector")]
            public Vector vector = null;
            [XmlElement("Color")]
            public ColorT color = null;
        }

        public class TypePath
        {
            [XmlAttribute]
            public string typeName;
            [XmlElement("Paths")]
            public List<string> Paths;
        }

        public class ColorT
        {
            public uint a;
            public uint b;
            public uint c;
            public uint d;
        }

        public class Vector
        {
            [XmlAttribute]
            public float a;
            [XmlAttribute]
            public float b;
            [XmlAttribute]
            public float c;
            [XmlAttribute]
            public float d;
        }
        #endregion

        #region Structs
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Header
        {
            public Magic magic;
            public short version;
            public short unk1;
            public int unk2;
            public int unk3;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct InfoHeader
        {
            public int count;
            public int size; //relative to 0x18
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct StructHeader
        {
            public uint Hash; //unknown from what; maybe CRC32 from string sum of this structure
            public int dataCount;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct DataEntry
        {
            public int relNameOffset;
            public ushort type;
            public short valueLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x20)]
            public byte[] unk1;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct NestedStructHeader
        {
            public short structRef;
            public short ID;
            public int size;
        }
        #endregion

        static void Main(string[] args)
        {
            if (args.Count() != 1)
            {
                Console.WriteLine("Usage: XFSConverter.exe <path>");
                Environment.Exit(0);
            }

            if (!File.Exists(args[0]))
            {
                Console.WriteLine("File " + args[0] + " doesn't exist!");
                Environment.Exit(0);
            }

            using (var br = new BinaryReaderX(File.OpenRead(args[0])))
            {
                //Header
                var header = br.ReadStruct<Header>();

                //Get Structure + Data
                var structInfoHeader = br.ReadStruct<InfoHeader>();
                var structInfo = br.ReadBytes(structInfoHeader.size);

                var paramInfoHeader = br.ReadStruct<InfoHeader>();
                var paramInfo = br.ReadBytes(paramInfoHeader.size);

                structBr = new BinaryReaderX(new MemoryStream(structInfo));
                paramBr = new BinaryReaderX(new MemoryStream(paramInfo));

                structOffs = structBr.ReadMultiple<int>(structInfoHeader.count);
            }

            while (structBr.BaseStream.Position <= structOffs.Last())
            {
                tree.Add(new Structure());
                var nextSection = GetStructure(tree.Last());
                structBr.BaseStream.Position = nextSection;
            }

            File.WriteAllText(args[0] + ".xml", ToXmlString(tree));
        }

        public static long GetStructure(Structure structObj)
        {
            var structHeader = structBr.ReadStruct<StructHeader>();
            var datas = structBr.ReadMultiple<DataEntry>(structHeader.dataCount);

            var nextStructOffset = structBr.BaseStream.Position;
            structObj.data = new List<Data>();
            foreach (var data in datas)
            {
                structBr.BaseStream.Position = data.relNameOffset;
                structObj.data.Add(new Data
                {
                    name = structBr.ReadCStringA(),
                    type = data.type,
                    dataLength = data.valueLength
                });

                var newOffset = GetDataCont(structObj.data.Last());
                if (newOffset > nextStructOffset)
                    nextStructOffset = newOffset;
            }

            return nextStructOffset;
        }

        public static long GetDataCont(Data data)
        {
            var count = paramBr.ReadInt32();

            //Checking integrity for type 0x1 and 0x2
            var typeInt = false;
            if ((data.type & 0xFF) == 0x01 || (data.type & 0xFF) == 0x02)
            {
                var check = paramBr.ReadUInt16();
                if ((check & 0x1) == 1)
                    typeInt = true;

                paramBr.BaseStream.Position -= 2;
            }

            if (typeInt)
            {
                if (data.structure == null) data.structure = new List<Structure>();
                long structOffset = 0;

                for (int i = 0; i < count; i++)
                {
                    var listHeader = paramBr.ReadStruct<NestedStructHeader>();

                    data.structure.Add(new Structure());
                    structBr.BaseStream.Position = structOffs[listHeader.structRef >> 1];
                    var tmp = GetStructure(data.structure.Last());

                    if (tmp > structOffset)
                        structOffset = tmp;
                }

                return structOffset;
            }
            else if (data.type == 0x8080)
            {
                data.typePath = new List<TypePath>();

                for (int i = 0; i < count; i++)
                {
                    var count2 = paramBr.ReadByte();
                    var typeName = paramBr.ReadCStringA();
                    var paths = new List<string>();
                    for (int j = 0; j < count2 - 1; j++)
                    {
                        paths.Add(paramBr.ReadCStringA());
                    }

                    data.typePath.Add(new TypePath
                    {
                        typeName = typeName,
                        Paths = paths
                    });
                }
            }
            else
            {
                data.Value = new List<string>();

                for (int i = 0; i < count; i++)
                {
                    switch (data.type & 0xFF)
                    {
                        case 0x01:
                            data.Value.Add(paramBr.ReadInt32().ToString());
                            break;
                        case 0x02:
                            data.Value.Add(paramBr.ReadInt32().ToString());
                            break;
                        case 0x03:
                            data.Value.Add(paramBr.ReadBoolean().ToString());
                            break;
                        case 0x04:
                            data.Value.Add(paramBr.ReadByte().ToString());
                            break;
                        case 0x06:
                            data.Value.Add(paramBr.ReadInt32().ToString());
                            break;
                        case 0x0a:
                            data.Value.Add(paramBr.ReadInt32().ToString());
                            break;
                        case 0x0c:
                            data.Value.Add(paramBr.ReadSingle().ToString(CultureInfo.InvariantCulture.NumberFormat));
                            break;
                        case 0x0e:
                            data.Value.Add(paramBr.ReadCStringA());
                            break;
                        case 0x14:
                            data.vector = new Vector
                            {
                                a = paramBr.ReadSingle(),
                                b = paramBr.ReadSingle(),
                                c = paramBr.ReadSingle(),
                                d = paramBr.ReadSingle(),
                            };
                            break;
                        case 0x15:
                            data.color = new ColorT
                            {
                                a = paramBr.ReadUInt32(),
                                b = paramBr.ReadUInt32(),
                                c = paramBr.ReadUInt32(),
                                d = paramBr.ReadUInt32(),
                            };
                            break;
                    }
                }
            }

            return structBr.BaseStream.Position;
        }

        public static string ToXmlString(List<Structure> obj)
        {
            var ns = new XmlSerializerNamespaces();
            ns.Add("", "");
            var xmlSettings = new XmlWriterSettings
            {
                Encoding = Encoding.UTF8,
                Indent = true,
                NewLineOnAttributes = false,
                IndentChars = "\t",
                CheckCharacters = false,
                OmitXmlDeclaration = true
            };
            using (var sw = new StringWriter())
            {
                new XmlSerializer(typeof(List<Structure>)).Serialize(XmlWriter.Create(sw, xmlSettings), obj, ns);
                return sw.ToString();
            }
        }
    }
}
