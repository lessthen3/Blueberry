/*******************************************************************
 *                        Bluberry v0.0.1
 *         Created by Ranyodh Singh Mandur - 🫐 2026-2026
 *
 *              Licensed under the MIT License (MIT).
 *         For more details, see the LICENSE file or visit:
 *               https://opensource.org/licenses/MIT
 *
 *           Bluberry is a free open source game engine
********************************************************************/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Blueberry;

//////////////////////////////////////////////////////////// Attribute ////////////////////////////////////////////////////////////

// tag any field that needs to persist across saves uwu
[AttributeUsage(AttributeTargets.Field)]
internal sealed class SavedStateAttribute : Attribute {} 

// marks an entire class/struct — all public and private fields 
// get serialized without needing [SavedState] on each one uwu
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
internal sealed class SerializeAllAttribute : Attribute {}

//////////////////////////////////////////////////////////// Serializer Status Codes >O< ////////////////////////////////////////////////////////////

internal static partial class BrewSerializer
{
    // cache per type — computed once, reused forever uwu
    record class TypeCacheEntry
    (
        FieldInfo[] Fields, 
        bool IsSerializable,    
        bool IsList,
        Type ListElementType,
        Action<BinaryWriter, object> WriteAction,   // null for serializable/list types
        Func<BinaryReader, object>   ReadAction     // null for serializable/list types
    )
    {
        // mutable — populated lazily on first serialize uwu
        public BakedField[] BakedFields = null;
        // compiled factory — replaces Activator.CreateInstance for both serializable and list types uwu
        public Func<object> Factory = null;

        // compiled list accessors — replaces PropertyInfo.GetValue and MethodInfo.Invoke uwu
        public Func<object, int> ListCount   = null; // list.Count
        public Func<object, int, object> ListGet     = null; // list[i]
        public Action<object, object> ListAdd     = null; // list.Add(element)

        // add to TypeCacheEntry uwu
        public Func<object, int, object> ArrayGet = null; // array[i]
        public Action<object, int, object> ArraySet = null; // array[i] = v
        public Func<object, int>            ArrayLength  = null;
        public Func<int, object>            ArrayFactory = null; // takes length, returns new array
    };

    record struct BakedField
    (
        Func<object, object>   Get,   // compiled IL getter
        Action<object, object> Set,   // compiled IL setter
        Action<BinaryWriter, object> Write,
        Func<BinaryReader, object>   Read
    );

    static readonly Dictionary<Type, TypeCacheEntry> pm_TypeCache = [];
    static readonly Dictionary<Type, bool> pm_UnmanagedCache = [];

    public enum StatusCode
	{
		OK,
		INVALID_RUN_DATA_PATH,
		VERSION_MISMATCH,
		BAD_RUN_DATA_MAGIC_NUMBER,
		BAD_USER_DATA_MAGIC_NUMBER
	}
}

//////////////////////////////////////////////////////////// Serializer ////////////////////////////////////////////////////////////

internal static partial class BrewSerializer
{
	//////////////////////////////////////////////////////////// Public API ////////////////////////////////////////////////////////////
    
    // writes all [SavedState] fields of fp_Object into fp_Writer as a length-prefixed blob uwu
    public static void
        Serialize(BinaryWriter fp_Writer, object fp_Object)
    {
		if(fp_Object == null)
		{
			return;
		}

        using MemoryStream f_TempStream = new();
        using BinaryWriter f_TempWriter = new(f_TempStream);

        // go through WriteAction so baked fields get used uwu
        GetTypeCache(fp_Object.GetType()).WriteAction(f_TempWriter, fp_Object);

        f_TempWriter.Flush();
        byte[] f_StateBytes = f_TempStream.ToArray();

        fp_Writer.Write(f_StateBytes.Length);
        fp_Writer.Write(f_StateBytes);
    }

    public static byte[]
        SerializeIntoBlob(object fp_Object)
    {
		if(fp_Object == null)
		{
			return null;
		}

        using MemoryStream f_TempStream = new();
        using BinaryWriter f_TempWriter = new(f_TempStream);

        // go through WriteAction so baked fields get used uwu
        GetTypeCache(fp_Object.GetType()).WriteAction(f_TempWriter, fp_Object);

        f_TempWriter.Flush();
        return f_TempStream.ToArray();
    }

    // reads a length-prefixed blob from fp_Reader and fills fp_Object's [SavedState] fields uwu
    public static void
        Deserialize(BinaryReader fp_Reader, object fp_Object)
    {
		if(fp_Object == null)
		{
			return;
		}

        int f_ByteCount = fp_Reader.ReadInt32();

        if (f_ByteCount == 0)
        {
            return;
        }

        byte[] f_StateBytes = fp_Reader.ReadBytes(f_ByteCount);

        using MemoryStream f_Stream = new(f_StateBytes);
        using BinaryReader f_Reader = new(f_Stream);

        // go through ReadAction — but we need to fill INTO fp_Object, not create new uwu
        // so we need a separate FillExisting path for top-level deserialize
        TypeCacheEntry f_Entry = GetTypeCache(fp_Object.GetType());
        EnsureBaked(f_Entry, f_Entry.Fields, fp_Object.GetType());

        for (int lv_Index = 0; lv_Index < f_Entry.BakedFields.Length; lv_Index++)
        {
            f_Entry.BakedFields[lv_Index].Set(fp_Object, f_Entry.BakedFields[lv_Index].Read(f_Reader));
        }
    }

    public static void
        Skip(BinaryReader fp_Reader)
    {
        int f_ByteCount = fp_Reader.ReadInt32();

        if (f_ByteCount > 0)
        {
            fp_Reader.ReadBytes(f_ByteCount);
        }
    }

    // reads the raw blob without deserializing, stash it for later if u need lazy restore OwO
    public static byte[]
        ReadRawBlob(BinaryReader fp_Reader)
    {
        int f_ByteCount = fp_Reader.ReadInt32();

        return f_ByteCount > 0 ? fp_Reader.ReadBytes(f_ByteCount) : [];
    }

    // deserialize directly from a previously stashed raw blob uwu
    public static void
        DeserializeFromBlob(byte[] fp_Blob, object fp_Object)
    {
        if (fp_Blob.Length == 0)
        {
            return;
        }

        using MemoryStream f_Stream = new(fp_Blob);
        using BinaryReader f_Reader = new(f_Stream);

        TypeCacheEntry f_Entry = GetTypeCache(fp_Object.GetType());
        EnsureBaked(f_Entry, f_Entry.Fields, fp_Object.GetType());

        for (int lv_Index = 0; lv_Index < f_Entry.BakedFields.Length; lv_Index++)
        {
            f_Entry.BakedFields[lv_Index].Set(fp_Object, f_Entry.BakedFields[lv_Index].Read(f_Reader));
        }
    }

	//////////////////////////////////////////////////////////// For top level Structs ////////////////////////////////////////////////////////////

    public static void
        Serialize<T>(BinaryWriter fp_Writer, in T fp_Value) where T : struct
    {
        if (IsUnmanagedType(typeof(T)))
        {
            // unmanaged — write size then raw bytes, zero temp buffer uwu
            ReadOnlySpan<byte> f_Bytes = MemoryMarshal.AsBytes(
                MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in fp_Value), 1)
            );
            fp_Writer.Write(f_Bytes.Length);
            fp_Writer.Write(f_Bytes);
            return;
        }

        // managed struct — box once, go through WriteAction uwu
        using MemoryStream f_TempStream = new();
        using BinaryWriter f_TempWriter = new(f_TempStream);

        GetTypeCache(typeof(T)).WriteAction(f_TempWriter, fp_Value);

        f_TempWriter.Flush();
        byte[] f_StateBytes = f_TempStream.ToArray();

        fp_Writer.Write(f_StateBytes.Length);
        fp_Writer.Write(f_StateBytes);
    }

    // struct deserialize — fills in place via ref, no boxing uwu
    public static void
        Deserialize<T>(BinaryReader fp_Reader, ref T fp_Object) where T : struct
    {
        int f_ByteCount = fp_Reader.ReadInt32();

        if (f_ByteCount == 0)
        {
            return;
        }

        byte[] f_StateBytes = fp_Reader.ReadBytes(f_ByteCount);

        using MemoryStream f_Stream = new(f_StateBytes);
        using BinaryReader f_Reader = new(f_Stream);

        // unmanaged fast path — bulk copy directly into the ref uwu
        if (IsUnmanagedType(typeof(T)))
        {
            Span<byte> f_Bytes = MemoryMarshal.AsBytes(
                MemoryMarshal.CreateSpan(ref fp_Object, 1)
            );
            f_Reader.Read(f_Bytes);
            return;
        }

        // managed struct — field by field via baked setters uwu
        // box ONCE to get a mutable reference, fill it, copy back via ref
        object f_Boxed = fp_Object;

        TypeCacheEntry f_Entry = GetTypeCache(typeof(T));
        EnsureBaked(f_Entry, f_Entry.Fields, typeof(T));

        for (int lv_Index = 0; lv_Index < f_Entry.BakedFields.Length; lv_Index++)
        {
            f_Entry.BakedFields[lv_Index].Set(f_Boxed, f_Entry.BakedFields[lv_Index].Read(f_Reader));
        }

        fp_Object = (T)f_Boxed; // copy mutated box back into ref uwu
    }

    // same for blob uwu
    public static void
        DeserializeFromBlob<T>(byte[] fp_Blob, ref T fp_Object) where T : struct
    {
        if (fp_Blob.Length == 0)
        {
            return;
        }

        if (IsUnmanagedType(typeof(T)))
        {
            using MemoryStream f_Stream = new(fp_Blob);
            using BinaryReader f_Reader = new(f_Stream);

            Span<byte> f_Bytes = MemoryMarshal.AsBytes(
                MemoryMarshal.CreateSpan(ref fp_Object, 1)
            );
            f_Reader.Read(f_Bytes);
            return;
        }

        object f_Boxed = fp_Object;

        using MemoryStream f_StreamM = new(fp_Blob);
        using BinaryReader f_ReaderM = new(f_StreamM);

        TypeCacheEntry f_Entry = GetTypeCache(typeof(T));
        EnsureBaked(f_Entry, f_Entry.Fields, typeof(T));

        for (int lv_Index = 0; lv_Index < f_Entry.BakedFields.Length; lv_Index++)
        {
            f_Entry.BakedFields[lv_Index].Set(f_Boxed, f_Entry.BakedFields[lv_Index].Read(f_ReaderM));
        }

        fp_Object = (T)f_Boxed;
    }
    
    //////////////////////////////////////////////////////////// Validation ////////////////////////////////////////////////////////////

	internal static bool
		ValidateBinary
		(
			string fp_AbsolutePath,
            uint fp_ExpectedMagic,
            ushort fp_ExpectedVersion,
			out BinaryReader ov_Reader
		)
	{
		ov_Reader = null;

		if (!File.Exists(fp_AbsolutePath))
		{
            Console.Instance.PrintError($"[SaveManager]: File does not exist >O<");
			return false;
		}

		FileStream f_Stream   = File.OpenRead(fp_AbsolutePath);
		ov_Reader = new(f_Stream);

		ushort f_SaveVersion = 0;

		uint f_Magic = ov_Reader.ReadUInt32();

		if (f_Magic != fp_ExpectedMagic)
		{
            ov_Reader.Dispose();
            ov_Reader = null;
            Console.Instance.PrintError($"[SAVE]: Magic mismatch in '{Path.GetFileName(fp_AbsolutePath)}'! " + $"Expected 0x{fp_ExpectedMagic:X8}, got 0x{f_Magic:X8} >O<");			
            return false;
		}

		f_SaveVersion = ov_Reader.ReadUInt16();

		if (f_SaveVersion != fp_ExpectedVersion)
		{
            ov_Reader.Dispose();
            ov_Reader = null;
			Console.Instance.PrintError($"[SAVE]: Version mismatch! Expected {fp_ExpectedVersion}, got {f_SaveVersion}");
			return false;
		}
		
		return true;
	}


    static bool
        EnsureDirectoryExists(string fp_AbsolutePath, bool fp_CreateIfMissing = false)
    {
        if (Directory.Exists(fp_AbsolutePath))
        {
            return true;
        }

        if (!fp_CreateIfMissing)
        {
            return false;
        }

        Directory.CreateDirectory(fp_AbsolutePath);

        return true;
    }

    //////////////////////////////////////////////////////////// File Helpers ////////////////////////////////////////////////////////////

    internal static void
        WriteFileSafe
        (
            string fp_AbsolutePath,  
            uint fp_MagicNumber, 
            ushort fp_VersionNumber, 
            Action<BinaryWriter> fp_WriteAction
        )
    {
        string f_TempPath = fp_AbsolutePath + ".tmp";

        EnsureDirectoryExists(Path.GetDirectoryName(fp_AbsolutePath), true);

        // write to temp first uwu
        using (FileStream f_Stream     = File.Open(f_TempPath, FileMode.Create))
        using (BinaryWriter f_Writer   = new(f_Stream))
        {
            f_Writer.Write(fp_MagicNumber);
            f_Writer.Write(fp_VersionNumber);
            fp_WriteAction(f_Writer);
        }

        // atomic swap — if game crashes before this line, old file is untouched
        // if it crashes after, new file is fully written uwu
        File.Move(f_TempPath, fp_AbsolutePath, overwrite: true);
    }

    // every read in the entire codebase goes through here uwu
    // returns false if file missing, bad magic, or version mismatch
    internal static bool
        ReadFileSafe
        (
            string fp_GodotPath, 
            uint fp_ExpectedMagicNumber,
            ushort fp_ExpectedVersion, 
            Action<BinaryReader> fp_ReadAction
        )
    {
        if (!ValidateBinary(fp_GodotPath, fp_ExpectedMagicNumber, fp_ExpectedVersion, out BinaryReader f_Reader))
        {
            return false;
        }

        using (f_Reader)
        {
            fp_ReadAction(f_Reader);
        }

        return true;
    }

    // read variant that returns a value instead of filling via side effect uwu
    internal static T
        ReadFileSafe<T>
        (
            string fp_GodotPath, 
            uint fp_ExpectedMagicNumber,
            ushort fp_ExpectedVersion, 
            Func<BinaryReader, T> fp_ReadFunc, 
            T fp_Default = default
        )
    {
        if (!ValidateBinary(fp_GodotPath, fp_ExpectedMagicNumber, fp_ExpectedVersion, out BinaryReader f_Reader))
        {
            return fp_Default;
        }

        using (f_Reader)
        {
            return fp_ReadFunc(f_Reader);
        }
    }

    // don't really feel like doing source generation atm so this'll do for unmanaged types ig
    static Action<BinaryWriter, object>
        BuildUnmanagedWriter(Type fp_Type)
    {
        MethodInfo f_Method = typeof(BrewSerializer)
            .GetMethod(nameof(WriteUnmanagedBoxed), BindingFlags.Static | BindingFlags.NonPublic)
            .MakeGenericMethod(fp_Type);

        var f_Writer = Expression.Parameter(typeof(BinaryWriter), "w");
        var f_Value  = Expression.Parameter(typeof(object), "v");

        return Expression.Lambda<Action<BinaryWriter, object>>
        (
            Expression.Call(f_Method, f_Writer, f_Value),
            f_Writer, f_Value
        ).Compile();
    }

    // T : unmanaged constraint means JIT emits the Span<byte> path with ZERO boxing inside >O<
    // the one unbox is unavoidable since we cross the object boundary here
    static void
        WriteUnmanagedBoxed<T>(BinaryWriter fp_Writer, object fp_BoxedValue) where T : unmanaged
    {
        T f_Value = (T)fp_BoxedValue; 
        ReadOnlySpan<byte> f_Bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref f_Value, 1));
        fp_Writer.Write(f_Bytes); // bulk copy, zero per-field overhead uwu
    }

    static Func<BinaryReader, object>
        BuildUnmanagedReader(Type fp_Type)
    {
        MethodInfo f_Method = typeof(BrewSerializer)
            .GetMethod(nameof(ReadUnmanagedBoxed), BindingFlags.Static | BindingFlags.NonPublic)
            .MakeGenericMethod(fp_Type);

        var f_Reader = Expression.Parameter(typeof(BinaryReader), "r");

        return Expression.Lambda<Func<BinaryReader, object>>
        (
            Expression.Call(f_Method, f_Reader),
            f_Reader
        ).Compile();
    }

    static object
        ReadUnmanagedBoxed<T>(BinaryReader fp_Reader) where T : unmanaged
    {
        T f_Result = default;

        Span<byte> f_Bytes = MemoryMarshal.AsBytes(
            MemoryMarshal.CreateSpan(ref f_Result, 1)
        );
        fp_Reader.Read(f_Bytes); // fills directly into f_Result memory uwu
        return f_Result; // one box on the way out
    }

    // check at cache build time whether this type is unmanaged uwu
    // if it is, emit a direct Span<byte> write instead of boxing
    static bool
        IsUnmanagedType(Type fp_Type)
    {
        if (pm_UnmanagedCache.TryGetValue(fp_Type, out bool ov_Cached))
        {
            return ov_Cached;
        }

        bool f_Result;

        if (fp_Type.IsPrimitive || fp_Type.IsEnum)
        {
            f_Result = true;
        }
        else if (fp_Type.IsValueType && !fp_Type.IsGenericType)
        {
            f_Result = fp_Type
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .All(f => IsUnmanagedType(f.FieldType));
        }
        else
        {
            f_Result = false;
        }

        pm_UnmanagedCache[fp_Type] = f_Result;
        return f_Result;
    }

	//////////////////////////////////////////////////////////// Compiled Reflection Actions ////////////////////////////////////////////////////////////
	// FieldInfo.GetValue path — what we have now:
    // managed call → reflection machinery → type check → unbox → return object
    // roughly 50-100ns per call with boxing overhead uwu

    // Expression tree compiled getter:
    // the .Compile() call emits actual IL that looks like:
    // ldarg.0 → castclass → ldfld → box → ret
    // roughly 5-10ns per call — same as a virtual method call uwu

    static Func<object, object>
        BuildGetter(FieldInfo fp_Field, Type fp_DeclaringType)
    {
        var f_Param   = Expression.Parameter(typeof(object), "instance");
        var f_Cast    = Expression.Convert(f_Param, fp_DeclaringType);       // (PlayerContext)instance
        var f_Field   = Expression.Field(f_Cast, fp_Field);                  // .TotalEssence
        var f_Box     = Expression.Convert(f_Field, typeof(object));         // box to object

        return Expression.Lambda<Func<object, object>>(f_Box, f_Param).Compile();
    }

    static Action<object, object>
        BuildSetter(FieldInfo fp_Field, Type fp_DeclaringType)
    {
        var f_Instance  = Expression.Parameter(typeof(object), "instance");
        var f_Value     = Expression.Parameter(typeof(object), "value");
        var f_Cast      = Expression.Convert(f_Instance, fp_DeclaringType);  // (PlayerContext)instance
        var f_FieldExpr = Expression.Field(f_Cast, fp_Field);                // .TotalEssence
        var f_Unbox     = Expression.Convert(f_Value, fp_Field.FieldType);   // (int)value
        var f_Assign    = Expression.Assign(f_FieldExpr, f_Unbox);           // .TotalEssence = (int)value

        return Expression.Lambda<Action<object, object>>(f_Assign, f_Instance, f_Value).Compile();
    }

    static void
        EnsureBaked(TypeCacheEntry fp_Entry, FieldInfo[] fp_Fields, Type fp_Type)
    {
        if (fp_Entry.BakedFields != null)
        {
            return;
        }

        fp_Entry.BakedFields = new BakedField[fp_Fields.Length];

        for (int lv_Index = 0; lv_Index < fp_Fields.Length; lv_Index++)
        {
            fp_Entry.BakedFields[lv_Index] = new BakedField
            (
                BuildGetter(fp_Fields[lv_Index], fp_Type),
                BuildSetter(fp_Fields[lv_Index], fp_Type),
                GetTypeCache(fp_Fields[lv_Index].FieldType).WriteAction,
                GetTypeCache(fp_Fields[lv_Index].FieldType).ReadAction
            );
        }
    }

    /// replaces Activator.CreateInstance — emits newobj IL directly uwu
    static Func<object>
        BuildFactory(Type fp_Type)
    {
        var f_New = Expression.New(fp_Type.GetConstructor(Type.EmptyTypes));
        var f_Box = Expression.Convert(f_New, typeof(object));

        return Expression.Lambda<Func<object>>(f_Box).Compile();
    }

    // replaces f_ListCount.GetValue — emits ldfld Count IL uwu
    static Func<object, int>
        BuildListCountGetter(Type fp_ListType)
    {
        var f_Param = Expression.Parameter(typeof(object), "list");
        var f_Cast  = Expression.Convert(f_Param, fp_ListType);
        var f_Count = Expression.Property(f_Cast, "Count");

        return Expression.Lambda<Func<object, int>>(f_Count, f_Param).Compile();
    }

    // replaces f_ListIndexer.GetValue — emits ldelem IL uwu
    static Func<object, int, object>
        BuildListIndexerGetter(Type fp_ListType, Type fp_ElementType)
    {
        var f_ListParam  = Expression.Parameter(typeof(object), "list");
        var f_IndexParam = Expression.Parameter(typeof(int),    "index");
        var f_Cast       = Expression.Convert(f_ListParam, fp_ListType);
        var f_Item       = Expression.Property(f_Cast, "Item", f_IndexParam);
        var f_Box        = Expression.Convert(f_Item, typeof(object));

        return Expression.Lambda<Func<object, int, object>>(f_Box, f_ListParam, f_IndexParam).Compile();
    }

    // replaces f_ListAdd.Invoke — emits callvirt Add IL uwu
    static Action<object, object>
        BuildListAdd(Type fp_ListType, Type fp_ElementType)
    {
        var f_ListParam = Expression.Parameter(typeof(object), "list");
        var f_ElemParam = Expression.Parameter(typeof(object), "element");
        var f_Cast      = Expression.Convert(f_ListParam, fp_ListType);
        var f_Unbox     = Expression.Convert(f_ElemParam, fp_ElementType);
        var f_Call      = Expression.Call(f_Cast, fp_ListType.GetMethod("Add"), f_Unbox);

        return Expression.Lambda<Action<object, object>>(f_Call, f_ListParam, f_ElemParam).Compile();
    }

    // replaces Array.GetValue — emits ldelem IL uwu
    static Func<object, int, object>
        BuildArrayGetter(Type fp_ArrayType, Type fp_ElementType)
    {
        var f_ArrayParam = Expression.Parameter(typeof(object), "array");
        var f_IndexParam = Expression.Parameter(typeof(int),    "index");
        var f_Cast       = Expression.Convert(f_ArrayParam, fp_ArrayType);  // (int[])array
        var f_Element    = Expression.ArrayIndex(f_Cast, f_IndexParam);     // array[index]
        var f_Box        = Expression.Convert(f_Element, typeof(object));

        return Expression.Lambda<Func<object, int, object>>(f_Box, f_ArrayParam, f_IndexParam).Compile();
    }

    // replaces Array.SetValue — emits stelem IL uwu
    static Action<object, int, object>
        BuildArraySetter(Type fp_ArrayType, Type fp_ElementType)
    {
        var f_ArrayParam = Expression.Parameter(typeof(object), "array");
        var f_IndexParam = Expression.Parameter(typeof(int),    "index");
        var f_ValueParam = Expression.Parameter(typeof(object), "value");
        var f_Cast       = Expression.Convert(f_ArrayParam, fp_ArrayType);
        var f_Unbox      = Expression.Convert(f_ValueParam, fp_ElementType);
        var f_Assign     = Expression.Assign(Expression.ArrayAccess(f_Cast, f_IndexParam), f_Unbox);

        return Expression.Lambda<Action<object, int, object>>(f_Assign, f_ArrayParam, f_IndexParam, f_ValueParam).Compile();
    }

    // replaces array.Length property — emits ldlen IL uwu
    static Func<object, int>
        BuildArrayLengthGetter(Type fp_ArrayType)
    {
        var f_Param  = Expression.Parameter(typeof(object), "array");
        var f_Cast   = Expression.Convert(f_Param, fp_ArrayType);
        var f_Length = Expression.ArrayLength(f_Cast); // emits ldlen directly uwu

        return Expression.Lambda<Func<object, int>>(f_Length, f_Param).Compile();
    }

    // replaces Array.CreateInstance — emits newarr IL uwu
    static Func<int, object>
        BuildArrayFactory(Type fp_ArrayType, Type fp_ElementType)
    {
        var f_LengthParam = Expression.Parameter(typeof(int), "length");
        var f_NewArray    = Expression.NewArrayBounds(fp_ElementType, f_LengthParam);
        var f_Box         = Expression.Convert(f_NewArray, typeof(object));

        return Expression.Lambda<Func<int, object>>(f_Box, f_LengthParam).Compile();
    }
	
    //////////////////////////////////////////////////////////// The Main Show OwO ////////////////////////////////////////////////////////////

    // gets all relevant [SavedState] or [SerializeAll] fields on the concrete type in declaration order uwu
    // MetadataToken gives declaration order reliably on .NET, Godot 4 uses .NET not Mono so we're good
    // single scan per type, re evaluating a type just hits the cache owo
    static TypeCacheEntry
        GetTypeCache(Type fp_Type)
    {
        if (pm_TypeCache.TryGetValue(fp_Type, out TypeCacheEntry ov_Cached))
        {
            return ov_Cached;
        }

        bool f_SerializeAll = fp_Type.GetCustomAttribute<SerializeAllAttribute>() != null;

        FieldInfo[] f_Fields = fp_Type
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where
            (
                f =>
                    f_SerializeAll
                    ? !f.IsInitOnly && f.GetCustomAttribute<NonSerializedAttribute>() == null
                    : f.GetCustomAttribute<SavedStateAttribute>() != null
            )
            .OrderBy(f => f.MetadataToken)
            .ToArray();

        // a type is serializable if SerializeAll is set OR it has at least one [SavedState] field uwu
        bool f_IsSerializable = f_SerializeAll || f_Fields.Length > 0;

        // serializable and list types recurse so they don't get a terminal action
        Action<BinaryWriter, object> f_WriteAction = null;
        Func<BinaryReader, object>   f_ReadAction  = null;

        // nested types >w<
        if (f_IsSerializable)    
        {
            TypeCacheEntry f_EntryRef = null; // set after f_Entry is created below

            f_WriteAction = (w, v) =>
            {
                EnsureBaked(f_EntryRef, f_Fields, fp_Type);

                // hot path — pure array iteration, zero dict lookups uwu
                for (int lv_Index = 0; lv_Index < f_EntryRef.BakedFields.Length; lv_Index++)
                {
                    f_EntryRef.BakedFields[lv_Index].Write(w, f_EntryRef.BakedFields[lv_Index].Get(v));
                }
            };

            f_ReadAction = r =>
            {    
                EnsureBaked(f_EntryRef, f_Fields, fp_Type);

                object f_Instance = f_EntryRef.Factory(); // no Activator.CreateInstance ^_^

                //pure array iteration, zero dict lookups uwu
                for (int lv_Index = 0; lv_Index < f_EntryRef.BakedFields.Length; lv_Index++)
                {
                    f_EntryRef.BakedFields[lv_Index].Set(f_Instance, f_EntryRef.BakedFields[lv_Index].Read(r));
                }

                return f_Instance;
            };

            TypeCacheEntry f_ObjectEntry = new(f_Fields, true, false, null, f_WriteAction, f_ReadAction)
            {
                Factory = BuildFactory(fp_Type) // compiled once uwu
            };

            f_EntryRef = f_ObjectEntry; 
            pm_TypeCache[fp_Type] = f_ObjectEntry;

            return f_ObjectEntry;
        }
        else if (fp_Type.IsArray)
        {
            Type f_ElementType = fp_Type.GetElementType();

            Func<object, int>           f_Length  = BuildArrayLengthGetter(fp_Type);
            Func<int, object>           f_Factory = BuildArrayFactory(fp_Type, f_ElementType);
            Func<object, int, object>   f_Get     = BuildArrayGetter(fp_Type, f_ElementType);
            Action<object, int, object> f_Set     = BuildArraySetter(fp_Type, f_ElementType);

            f_WriteAction = (w, v) =>
            {
                int f_Len             = f_Length(v);
                TypeCacheEntry f_Elem = GetTypeCache(f_ElementType);

                w.Write(f_Len);

                for (int lv_Index = 0; lv_Index < f_Len; lv_Index++)
                {
                    f_Elem.WriteAction(w, f_Get(v, lv_Index));
                }
            };

            f_ReadAction = r =>
            {
                int f_Len             = r.ReadInt32();
                object f_Array        = f_Factory(f_Len); // compiled newarr uwu
                TypeCacheEntry f_Elem = GetTypeCache(f_ElementType);

                for (int lv_Index = 0; lv_Index < f_Len; lv_Index++)
                {
                    f_Set(f_Array, lv_Index, f_Elem.ReadAction(r)); // compiled stelem uwu
                }

                return f_Array;
            };

            TypeCacheEntry f_ArrayEntry = new(f_Fields, false, false, null, f_WriteAction, f_ReadAction)
            {
                ArrayLength  = f_Length,
                ArrayFactory = f_Factory,
                ArrayGet     = f_Get,
                ArraySet     = f_Set
            };

            pm_TypeCache[fp_Type] = f_ArrayEntry;
            return f_ArrayEntry;
        }
        else if (fp_Type.IsGenericType && fp_Type.GetGenericTypeDefinition() == typeof(List<>))
        {
            Type f_ElementType = fp_Type.GetGenericArguments()[0];
            
            // compile all four accessors once which means zero raw reflection in the hot path OwO
            Func<object>             f_Factory   = BuildFactory(fp_Type);
            Func<object, int>        f_Count     = BuildListCountGetter(fp_Type);
            Func<object, int, object> f_Get   = BuildListIndexerGetter(fp_Type, f_ElementType);
            Action<object, object>   f_Add    = BuildListAdd(fp_Type, f_ElementType);

            f_WriteAction = (w, v) =>
            {  
                int f_ListCount = f_Count(v);

                w.Write(f_ListCount); // int32 count first, same pattern as everywhere else uwu

                TypeCacheEntry f_TypeInfo = GetTypeCache(f_ElementType);

                for (int lv_Index = 0; lv_Index < f_ListCount; lv_Index++)
                {
                    f_TypeInfo.WriteAction(w, f_Get(v, lv_Index));
                }
            };

            f_ReadAction = r =>
            {
                int f_ListCount = r.ReadInt32();
                object f_List   = f_Factory(); // compiled newobj uwu

                TypeCacheEntry f_TypeInfo = GetTypeCache(f_ElementType);

                for (int lv_Index = 0; lv_Index < f_ListCount; lv_Index++)
                {
                    f_Add(f_List, f_TypeInfo.ReadAction(r));
                }

                return f_List;
            };

            TypeCacheEntry f_ListEntry = new(f_Fields, false, true, f_ElementType, f_WriteAction, f_ReadAction)
            {
                Factory   = f_Factory,
                ListCount = f_Count,
                ListGet   = f_Get,
                ListAdd   = f_Add
            };

            pm_TypeCache[fp_Type] = f_ListEntry;

            return f_ListEntry;
        }
        else //just whatever is left probably a var if not it throws an exception owo
        {
            if (fp_Type.IsEnum)
            {
                f_WriteAction = (w, v) => w.Write(Convert.ToByte(v));
                f_ReadAction  = r => Enum.ToObject(fp_Type, r.ReadByte());
            }
            else
            {
                switch (Type.GetTypeCode(fp_Type))
                {
                    case TypeCode.String:
                    {
                        f_WriteAction = (w, v) =>
                        {
                            if (v == null)
                            {
                                w.Write(false); // null flag uwu
                                return;
                            }
                            w.Write(true);
                            w.Write((string)v);
                        };

                        f_ReadAction = r =>
                        {
                            bool f_HasValue = r.ReadBoolean();
                            return f_HasValue ? r.ReadString() : null;
                        };
                    }
                    break;
                    case TypeCode.Double:  
                    { 
                        f_WriteAction =  (w, v) => w.Write((double)v); 
                        f_ReadAction = r => r.ReadDouble();
                    }                    
                    break;
                    case TypeCode.Single:  
                    { 
                        f_WriteAction =  (w, v) => w.Write((float)v); 
                        f_ReadAction = r => r.ReadSingle();
                    }                    
                    break;
                    case TypeCode.Int32:   
                    { 
                        f_WriteAction =  (w, v) => w.Write((int)v); 
                        f_ReadAction = r => r.ReadInt32();
                    }                    
                    break;
                    case TypeCode.UInt32: 
                    { 
                        f_WriteAction =  (w, v) => w.Write((uint)v); 
                        f_ReadAction = r => r.ReadUInt32();
                    }                    
                    break;
                    case TypeCode.Int64:  
                    { 
                        f_WriteAction =  (w, v) => w.Write((long)v); 
                        f_ReadAction = r => r.ReadInt64();
                    }                    
                    break;
                    case TypeCode.UInt64:  
                    { 
                        f_WriteAction =  (w, v) => w.Write((ulong)v); 
                        f_ReadAction = r => r.ReadUInt64();
                    }                    
                    break;
                    case TypeCode.Int16:   
                    { 
                        f_WriteAction =  (w, v) => w.Write((short)v); 
                        f_ReadAction = r => r.ReadInt16();
                    }                    
                    break;
                    case TypeCode.UInt16:  
                    { 
                        f_WriteAction =  (w, v) => w.Write((ushort)v); 
                        f_ReadAction = r => r.ReadUInt16();
                    }                    
                    break;
                    case TypeCode.Byte:    
                    { 
                        f_WriteAction =  (w, v) => w.Write((byte)v); 
                        f_ReadAction = r => r.ReadByte();
                    }                    
                    break;
                    case TypeCode.Boolean: 
                    { 
                        f_WriteAction =  (w, v) => w.Write((bool)v); 
                        f_ReadAction = r => r.ReadBoolean();
                    }                    
                    break;

                    default:
                        throw new NotSupportedException($"[SAVE]: [SavedState] on unsupported type {fp_Type.Name} — only primitives and enums uwu");
                }
            }

            TypeCacheEntry f_VariableEntry = new(f_Fields, false, false, null, f_WriteAction, f_ReadAction);
            pm_TypeCache[fp_Type] = f_VariableEntry;

            return f_VariableEntry;
        }
    }
}
