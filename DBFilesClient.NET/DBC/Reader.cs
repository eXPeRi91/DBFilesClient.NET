﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Sigil;

namespace DBFilesClient.NET.DBC
{
    internal sealed class Reader<T> : Reader where T : class, new()
    {
        private int _recordSize;
        private int _recordCount;

        #region Emit Helpers
        // ReSharper disable once StaticMemberInGenericType
        private static Dictionary<TypeCode, MethodInfo> _binaryReaderMethods = new Dictionary<TypeCode, MethodInfo>()
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
        #endregion

        internal Reader(Stream fileStream) : base(fileStream)
        {
            // We get to this through the Factory, meaning we already read the signature...
            _recordCount = ReadInt32();
            var fieldCount = ReadInt32(); // Counts arrays
            _recordSize = ReadInt32();
            var stringBlockSize = ReadInt32();

            // Generate the record loader function now.
            _loader = GenerateRecordLoader();

            var recordPosition = BaseStream.Position;
            for (var i = 0; i < _recordCount; ++i)
            {
                LoadRecord();
                recordPosition += _recordSize;
                BaseStream.Position = recordPosition;
            }

        }

        private delegate T LoaderDelegate(Reader<T> table);
        private LoaderDelegate _loader;

        private void LoadRecord()
        {
            var key = ReadInt32();
            BaseStream.Position -= 4;
            TriggerRecordLoaded(key, _loader(this));
        }

        private LoaderDelegate GenerateRecordLoader()
        {
            // This is here strictly for debugging (saves to an assembly that can be loaded in ilspy et al)
            /*var asmName = new AssemblyName("DynamicCreateAssembly");
            var asm = AppDomain.CurrentDomain.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.RunAndSave);
            var mod = asm.DefineDynamicModule(asmName.Name, asmName.Name + ".dll");
            var typeBuilder = mod.DefineType("MyType", TypeAttributes.Public | TypeAttributes.Class);
            //var method = typeBuilder.DefineMethod("DynamicCreate_T", MethodAttributes.Static | MethodAttributes.Public,
            //   typeof (T), new[] { typeof(DBFileBinaryReader), typeof(DB2<T>) });
            var emitter = Emit<LoaderDelegate>.BuildMethod(typeBuilder, "DynamicCreate_T",
                MethodAttributes.Static | MethodAttributes.Public, CallingConventions.Standard);*/

            var emitter = Emit<LoaderDelegate>.NewDynamicMethod("LoaderDelegate", null, false);
            var resultLocal = emitter.DeclareLocal<T>();
            emitter.NewObject<T>();
            emitter.StoreLocal(resultLocal);

            var fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var fieldInfo in fields)
            {
                var fieldType = fieldInfo.FieldType;
                var isArray = fieldInfo.FieldType.IsArray;
                var typeCode = Type.GetTypeCode(isArray ? fieldType.GetElementType() : fieldType);

                var callVirt = _binaryReaderMethods[typeCode];

                if (!isArray)
                {
                    emitter.LoadLocal(resultLocal);
                    emitter.LoadArgument(0);
                    emitter.CallVirtual(callVirt);
                    emitter.StoreField(fieldInfo);
                }
                else
                {
                    var marshalAttribute = fieldInfo.GetCustomAttribute<MarshalAsAttribute>();
                    if (marshalAttribute == null)
                        throw new InvalidStructureException($"Field {fieldInfo.Name} is an array but misses MarshalAsAttribute!");

                    emitter.LoadLocal(resultLocal);
                    emitter.LoadConstant(marshalAttribute.SizeConst);
                    emitter.NewArray(fieldType.GetElementType());
                    emitter.StoreField(fieldInfo);

                    var loopBodyLabel = emitter.DefineLabel();
                    var loopConditionLabel = emitter.DefineLabel();

                    using (var iterationLocal = emitter.DeclareLocal<int>())
                    {
                        emitter.LoadConstant(0);
                        emitter.StoreLocal(iterationLocal);
                        emitter.Branch(loopConditionLabel);
                        emitter.MarkLabel(loopBodyLabel);
                        emitter.LoadLocal(resultLocal);
                        emitter.LoadField(fieldInfo);
                        emitter.LoadLocal(iterationLocal);
                        emitter.LoadArgument(0);
                        emitter.CallVirtual(callVirt);
                        emitter.StoreElement(fieldType.GetElementType());
                        emitter.LoadLocal(iterationLocal);
                        emitter.LoadConstant(1);
                        emitter.Add();
                        emitter.StoreLocal(iterationLocal);
                        emitter.MarkLabel(loopConditionLabel);
                        emitter.LoadLocal(iterationLocal);
                        emitter.LoadConstant(marshalAttribute.SizeConst);
                        emitter.CompareLessThan();
                        emitter.BranchIfTrue(loopBodyLabel);
                    }
                }
            }

            emitter.LoadLocal(resultLocal);
            emitter.Return();

            /*emiter.CreateMethod();
            typeBuilder.CreateType();
            asm.Save(asmName.Name + ".dll");*/

            return emitter.CreateDelegate();
        }

        public override string ReadInlineString()
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

        public override string ReadTableString()
        {
            // Store position of the next field in this record.
            var oldPosition = BaseStream.Position + 4;

            // Compute offset to string in table.
            BaseStream.Position = ReadInt32() + 20 + _recordSize * _recordCount;

            // Read the string inline.
            var stringValue = ReadInlineString();

            // Restore stream position.
            BaseStream.Position = oldPosition;
            return stringValue;
        }
    }
}
