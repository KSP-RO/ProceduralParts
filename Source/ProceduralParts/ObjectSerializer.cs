using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace ProceduralParts
{

    internal static class ObjectSerializer
    {

        internal static byte[] Serialize<T>(T obj)
        {
            MemoryStream stream = new MemoryStream();
            using (stream)
            {
                BinaryFormatter fmt = new BinaryFormatter();
                fmt.Serialize(stream, obj);
            }
            return stream.ToArray();
        }

        internal static void Deserialize<T>(byte[] data, out T value)
        {
            using (MemoryStream stream = new MemoryStream(data))
            {
                BinaryFormatter fmt = new BinaryFormatter();
                value = (T)fmt.Deserialize(stream);
            }
        }
    }

}