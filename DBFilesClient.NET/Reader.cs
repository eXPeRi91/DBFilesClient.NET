﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using DBFilesClient.NET.WDB5;

namespace DBFilesClient.NET
{
    internal abstract class Reader : BinaryReader
    {
        protected long StringTableOffset { private get; set; }

        private static Dictionary<TypeCode, MethodInfo> _binaryReaderMethods = new Dictionary<TypeCode, MethodInfo>
        {
            { TypeCode.Int64, typeof (BinaryReader).GetMethod("ReadInt64", Type.EmptyTypes) },
            { TypeCode.Int32, typeof (BinaryReader).GetMethod("ReadInt32", Type.EmptyTypes) },
            { TypeCode.Int16, typeof (BinaryReader).GetMethod("ReadInt16", Type.EmptyTypes) },
            { TypeCode.SByte, typeof (BinaryReader).GetMethod("ReadSByte", Type.EmptyTypes) },

            { TypeCode.UInt64, typeof (BinaryReader).GetMethod("ReadUInt64", Type.EmptyTypes) },
            { TypeCode.UInt32, typeof (BinaryReader).GetMethod("ReadUInt32", Type.EmptyTypes) },
            { TypeCode.UInt16, typeof (BinaryReader).GetMethod("ReadUInt16", Type.EmptyTypes) },
            { TypeCode.Byte, typeof (BinaryReader).GetMethod("ReadByte", Type.EmptyTypes) },

            { TypeCode.Char, typeof (BinaryReader).GetMethod("ReadChar", Type.EmptyTypes) },
            { TypeCode.Single, typeof (BinaryReader).GetMethod("ReadSingle", Type.EmptyTypes) },

            { TypeCode.String, typeof (Reader).GetMethod("ReadTableString", Type.EmptyTypes) }
        };

        protected static MethodInfo GetPrimitiveLoader(Type type)
        {
            while (type.IsArray)
                type = type.GetElementType();

            MethodInfo methodInfo;
            _binaryReaderMethods.TryGetValue(Type.GetTypeCode(type), out methodInfo);
            return methodInfo;
        }

        protected static MethodInfo GetPrimitiveLoader<T>() => GetPrimitiveLoader(typeof (T));

        protected static MethodInfo GetPrimitiveLoader(FieldInfo fieldInfo) => GetPrimitiveLoader(fieldInfo.FieldType);
        protected static MethodInfo GetPrimitiveLoader(PropertyInfo propertyInfo) => GetPrimitiveLoader(propertyInfo.PropertyType);

        // ReSharper disable once MemberCanBeProtected.Global
        public string ReadInlineString()
        {
            var stringStart = BaseStream.Position;
            var stringLength = 0;
            while (ReadByte() != '\0')
                ++stringLength;
            BaseStream.Position = stringStart;

            if (stringLength == 0)
                return string.Empty;

            var stringValue = Encoding.UTF8.GetString(ReadBytes(stringLength));
            ReadByte();

            return stringValue;
        }

        // ReSharper disable once MemberCanBeProtected.Global
        // ReSharper disable once UnusedMemberHiearchy.Global
        public virtual string ReadTableString()
        {
            // Store position of the next field in this record.
            var oldPosition = BaseStream.Position + 4;

            // Compute offset to string in table.
            BaseStream.Position = ReadInt32() + StringTableOffset;

            // Read the string inline.
            var stringValue = ReadInlineString();

            // Restore stream position.
            BaseStream.Position = oldPosition;
            return stringValue;
        }

        public event Action<int, object> OnRecordLoaded;

        protected void TriggerRecordLoaded(int key, object record) => OnRecordLoaded?.Invoke(key, record);

        protected Reader(Stream data) : base(data)
        {
            Debug.Assert(data is MemoryStream);
        }

        internal abstract void Load();

        // ReSharper disable once MemberCanBeProtected.Global
        public int ReadInt24()
        {
            return ReadByte() | (ReadByte() << 8) | (ReadByte() << 16);
        }

        // ReSharper disable once UnusedMember.Global
        public uint ReadUInt24() => (uint)ReadInt24();
    }
}
