﻿/*
Copyright © 2011, Kristen Mallory DBA klink.
All rights reserved.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace BESSy.Serialization.Converters
{
    [Serializable]
    public class BinConverterGuid : IBinConverter<Guid>
    {
        static readonly Guid _maxGuid = new Guid(new byte[16] { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 });

        public byte[] ToBytes(Guid item)
        {
            return item.ToByteArray();
        }

        public Guid FromBytes(byte[] bytes)
        {
            return new Guid(bytes);
        }

        public Guid FromStream(Stream inStream)
        {
            var bytes = new byte[Length];

            inStream.Read(bytes, 0, bytes.Length);

            return FromBytes(bytes);
        }

        public Guid Min { get { return Guid.Empty; } }
        public Guid Max { get { return _maxGuid; } }

        public int Length
        {
            get { return 16; }
        }

        public int Compare(Guid item1, Guid item2)
        {
            return item1.CompareTo(item2);
        }

    }
}
