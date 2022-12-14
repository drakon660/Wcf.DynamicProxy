// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;


namespace Microsoft.Xml
{
    public abstract class ArrayHelper<TArgument, TArray>
    {
        public TArray[] ReadArray(XmlDictionaryReader reader, TArgument localName, TArgument namespaceUri, int maxArrayLength)
        {
            TArray[][] arrays = null;
            TArray[] array = null;
            int arrayCount = 0;
            int totalRead = 0;
            int count;
            if (reader.TryGetArrayLength(out count))
            {
                if (count > XmlDictionaryReader.MaxInitialArrayLength)
                    count = XmlDictionaryReader.MaxInitialArrayLength;
            }
            else
            {
                count = 32;
            }
            while (true)
            {
                array = new TArray[count];
                int read = 0;
                while (read < array.Length)
                {
                    int actual = ReadArray(reader, localName, namespaceUri, array, read, array.Length - read);
                    if (actual == 0)
                        break;
                    read += actual;
                }
                totalRead += read;
                if (read < array.Length || reader.NodeType == XmlNodeType.EndElement)
                    break;
                if (arrays == null)
                    arrays = new TArray[32][];
                arrays[arrayCount++] = array;
                count = count * 2;
            }
            if (totalRead != array.Length || arrayCount > 0)
            {
                TArray[] newArray = new TArray[totalRead];
                int offset = 0;
                for (int i = 0; i < arrayCount; i++)
                {
                    Array.Copy(arrays[i], 0, newArray, offset, arrays[i].Length);
                    offset += arrays[i].Length;
                }
                Array.Copy(array, 0, newArray, offset, totalRead - offset);
                array = newArray;
            }
            return array;
        }

        public void WriteArray(XmlDictionaryWriter writer, string prefix, TArgument localName, TArgument namespaceUri, XmlDictionaryReader reader)
        {
            int count;
            if (reader.TryGetArrayLength(out count))
                count = Math.Min(count, 256);
            else
                count = 256;
            TArray[] array = new TArray[count];
            while (true)
            {
                int actual = ReadArray(reader, localName, namespaceUri, array, 0, array.Length);
                if (actual == 0)
                    break;
                WriteArray(writer, prefix, localName, namespaceUri, array, 0, actual);
            }
        }

        protected abstract int ReadArray(XmlDictionaryReader reader, TArgument localName, TArgument namespaceUri, TArray[] array, int offset, int count);
        protected abstract void WriteArray(XmlDictionaryWriter writer, string prefix, TArgument localName, TArgument namespaceUri, TArray[] array, int offset, int count);
    }

    // Supported array types
    // bool
    // Int16
    // Int32
    // Int64
    // Float
    // Double
    // Decimal
    // DateTime
    // Guid
    // TimeSpan

    // Int8 is not supported since sbyte[] is non-cls compliant, and uncommon
    // UniqueId is not supported since elements may be variable size strings

    internal class BooleanArrayHelperWithString : ArrayHelper<string, bool>
    {
        static public readonly BooleanArrayHelperWithString Instance = new BooleanArrayHelperWithString();

        protected override int ReadArray(XmlDictionaryReader reader, string localName, string namespaceUri, bool[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, string localName, string namespaceUri, bool[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }

    public class BooleanArrayHelperWithDictionaryString : ArrayHelper<XmlDictionaryString, bool>
    {
        static public readonly BooleanArrayHelperWithDictionaryString Instance = new BooleanArrayHelperWithDictionaryString();

        protected override int ReadArray(XmlDictionaryReader reader, XmlDictionaryString localName, XmlDictionaryString namespaceUri, bool[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, XmlDictionaryString localName, XmlDictionaryString namespaceUri, bool[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }

    public class Int16ArrayHelperWithString : ArrayHelper<string, Int16>
    {
        static public readonly Int16ArrayHelperWithString Instance = new Int16ArrayHelperWithString();

        protected override int ReadArray(XmlDictionaryReader reader, string localName, string namespaceUri, Int16[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, string localName, string namespaceUri, Int16[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }

    public class Int16ArrayHelperWithDictionaryString : ArrayHelper<XmlDictionaryString, Int16>
    {
        static public readonly Int16ArrayHelperWithDictionaryString Instance = new Int16ArrayHelperWithDictionaryString();

        protected override int ReadArray(XmlDictionaryReader reader, XmlDictionaryString localName, XmlDictionaryString namespaceUri, Int16[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, XmlDictionaryString localName, XmlDictionaryString namespaceUri, Int16[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }

    public class Int32ArrayHelperWithString : ArrayHelper<string, Int32>
    {
        static public readonly Int32ArrayHelperWithString Instance = new Int32ArrayHelperWithString();

        protected override int ReadArray(XmlDictionaryReader reader, string localName, string namespaceUri, Int32[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, string localName, string namespaceUri, Int32[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }

    public class Int32ArrayHelperWithDictionaryString : ArrayHelper<XmlDictionaryString, Int32>
    {
        static public readonly Int32ArrayHelperWithDictionaryString Instance = new Int32ArrayHelperWithDictionaryString();

        protected override int ReadArray(XmlDictionaryReader reader, XmlDictionaryString localName, XmlDictionaryString namespaceUri, Int32[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, XmlDictionaryString localName, XmlDictionaryString namespaceUri, Int32[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }

    public class Int64ArrayHelperWithString : ArrayHelper<string, Int64>
    {
        static public readonly Int64ArrayHelperWithString Instance = new Int64ArrayHelperWithString();

        protected override int ReadArray(XmlDictionaryReader reader, string localName, string namespaceUri, Int64[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, string localName, string namespaceUri, Int64[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }

    public class Int64ArrayHelperWithDictionaryString : ArrayHelper<XmlDictionaryString, Int64>
    {
        static public readonly Int64ArrayHelperWithDictionaryString Instance = new Int64ArrayHelperWithDictionaryString();

        protected override int ReadArray(XmlDictionaryReader reader, XmlDictionaryString localName, XmlDictionaryString namespaceUri, Int64[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, XmlDictionaryString localName, XmlDictionaryString namespaceUri, Int64[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }

    public class SingleArrayHelperWithString : ArrayHelper<string, float>
    {
        static public readonly SingleArrayHelperWithString Instance = new SingleArrayHelperWithString();

        protected override int ReadArray(XmlDictionaryReader reader, string localName, string namespaceUri, float[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, string localName, string namespaceUri, float[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }

    public class SingleArrayHelperWithDictionaryString : ArrayHelper<XmlDictionaryString, float>
    {
        static public readonly SingleArrayHelperWithDictionaryString Instance = new SingleArrayHelperWithDictionaryString();

        protected override int ReadArray(XmlDictionaryReader reader, XmlDictionaryString localName, XmlDictionaryString namespaceUri, float[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, XmlDictionaryString localName, XmlDictionaryString namespaceUri, float[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }

    public class DoubleArrayHelperWithString : ArrayHelper<string, double>
    {
        static public readonly DoubleArrayHelperWithString Instance = new DoubleArrayHelperWithString();

        protected override int ReadArray(XmlDictionaryReader reader, string localName, string namespaceUri, double[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, string localName, string namespaceUri, double[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }

    public class DoubleArrayHelperWithDictionaryString : ArrayHelper<XmlDictionaryString, double>
    {
        static public readonly DoubleArrayHelperWithDictionaryString Instance = new DoubleArrayHelperWithDictionaryString();

        protected override int ReadArray(XmlDictionaryReader reader, XmlDictionaryString localName, XmlDictionaryString namespaceUri, double[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, XmlDictionaryString localName, XmlDictionaryString namespaceUri, double[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }

    public class DecimalArrayHelperWithString : ArrayHelper<string, decimal>
    {
        static public readonly DecimalArrayHelperWithString Instance = new DecimalArrayHelperWithString();

        protected override int ReadArray(XmlDictionaryReader reader, string localName, string namespaceUri, decimal[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, string localName, string namespaceUri, decimal[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }

    public class DecimalArrayHelperWithDictionaryString : ArrayHelper<XmlDictionaryString, decimal>
    {
        static public readonly DecimalArrayHelperWithDictionaryString Instance = new DecimalArrayHelperWithDictionaryString();

        protected override int ReadArray(XmlDictionaryReader reader, XmlDictionaryString localName, XmlDictionaryString namespaceUri, decimal[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, XmlDictionaryString localName, XmlDictionaryString namespaceUri, decimal[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }

    public class DateTimeArrayHelperWithString : ArrayHelper<string, DateTime>
    {
        static public readonly DateTimeArrayHelperWithString Instance = new DateTimeArrayHelperWithString();

        protected override int ReadArray(XmlDictionaryReader reader, string localName, string namespaceUri, DateTime[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, string localName, string namespaceUri, DateTime[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }

    public class DateTimeArrayHelperWithDictionaryString : ArrayHelper<XmlDictionaryString, DateTime>
    {
        static public readonly DateTimeArrayHelperWithDictionaryString Instance = new DateTimeArrayHelperWithDictionaryString();

        protected override int ReadArray(XmlDictionaryReader reader, XmlDictionaryString localName, XmlDictionaryString namespaceUri, DateTime[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, XmlDictionaryString localName, XmlDictionaryString namespaceUri, DateTime[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }

    public class GuidArrayHelperWithString : ArrayHelper<string, Guid>
    {
        static public readonly GuidArrayHelperWithString Instance = new GuidArrayHelperWithString();

        protected override int ReadArray(XmlDictionaryReader reader, string localName, string namespaceUri, Guid[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, string localName, string namespaceUri, Guid[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }

    internal class GuidArrayHelperWithDictionaryString : ArrayHelper<XmlDictionaryString, Guid>
    {
        static public readonly GuidArrayHelperWithDictionaryString Instance = new GuidArrayHelperWithDictionaryString();

        protected override int ReadArray(XmlDictionaryReader reader, XmlDictionaryString localName, XmlDictionaryString namespaceUri, Guid[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, XmlDictionaryString localName, XmlDictionaryString namespaceUri, Guid[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }

    internal class TimeSpanArrayHelperWithString : ArrayHelper<string, TimeSpan>
    {
        static public readonly TimeSpanArrayHelperWithString Instance = new TimeSpanArrayHelperWithString();

        protected override int ReadArray(XmlDictionaryReader reader, string localName, string namespaceUri, TimeSpan[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, string localName, string namespaceUri, TimeSpan[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }

    internal class TimeSpanArrayHelperWithDictionaryString : ArrayHelper<XmlDictionaryString, TimeSpan>
    {
        static public readonly TimeSpanArrayHelperWithDictionaryString Instance = new TimeSpanArrayHelperWithDictionaryString();

        protected override int ReadArray(XmlDictionaryReader reader, XmlDictionaryString localName, XmlDictionaryString namespaceUri, TimeSpan[] array, int offset, int count)
        {
            return reader.ReadArray(localName, namespaceUri, array, offset, count);
        }

        protected override void WriteArray(XmlDictionaryWriter writer, string prefix, XmlDictionaryString localName, XmlDictionaryString namespaceUri, TimeSpan[] array, int offset, int count)
        {
            writer.WriteArray(prefix, localName, namespaceUri, array, offset, count);
        }
    }
}
