using System.Runtime.InteropServices;

namespace OblivionVoice.Client.Animation;

// Reflection subset of the locally proven Woodcutting/UI UE4SS bridge. Owned facial montages only.
// Never uses raw game offsets, body-animation replacement, or drawing calls.
internal sealed class NativeFacialBridge
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate nint FindObject(nint cls, nint outer, string name, [MarshalAs(UnmanagedType.I1)] bool exact);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate nint Named(nint self, string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Ref(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint NameIndex(nint name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Event(nint self, nint fn, nint args);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Free(nint allocation);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string name);

    private readonly FindObject _find;
    private readonly Named _property, _function;
    private readonly Ref _offset, _elementSize, _paramSize, _children, _next, _name;
    private readonly NameIndex _index;
    private readonly Event _event;
    private readonly Free _free;
    private readonly Dictionary<string, NativeFunctionLayout> _contracts;
    private readonly Dictionary<(string, nint), NativeFunctionLayout> _resolved = [];
    private readonly Dictionary<string, uint> _fieldNames = new(StringComparer.Ordinal);
    private int _nameWidth;
    private readonly NativeScriptObjectCache _scriptObjects = new();
    private readonly Func<string,nint> _findPath;

    public NativeFacialBridge()
    {
        _findPath = FindObjectPath;
        nint module = GetModuleHandle("UE4SS.dll");
        if (module == 0) throw new NotSupportedException("UE4SS is not loaded.");
        T Bind<T>(string symbol) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, symbol));
        _find = Bind<FindObject>("?StaticFindObject_InternalSlow@UObjectGlobals@Unreal@RC@@YAPEAVUObject@23@PEAVUClass@23@PEAV423@PEB_W_N@Z");
        _property = Bind<Named>("?GetPropertyByNameInChain@UObject@Unreal@RC@@QEAAPEAVFProperty@23@PEB_W@Z");
        _function = Bind<Named>("?GetFunctionByNameInChain@UObject@Unreal@RC@@QEAAPEAVUFunction@23@PEB_W@Z");
        _offset = Bind<Ref>("?GetOffset_Internal@FProperty@Unreal@RC@@QEAAAEAHXZ");
        _elementSize = Bind<Ref>("?GetElementSize@FProperty@Unreal@RC@@QEAAAEAHXZ");
        _paramSize = Bind<Ref>("?GetParmsSize@UFunction@Unreal@RC@@QEAAAEAGXZ");
        _children = Bind<Ref>("?GetChildProperties@UStruct@Unreal@RC@@QEAAAEAPEAVFField@23@XZ");
        _next = Bind<Ref>("?GetNext@FField@Unreal@RC@@AEAAAEAPEAV123@XZ");
        _name = Bind<Ref>("?GetNamePrivate@FField@Unreal@RC@@AEAAAEAVFName@23@XZ");
        _index = Bind<NameIndex>("?GetComparisonIndex@FName@Unreal@RC@@QEBAIXZ");
        _event = Bind<Event>("?ProcessEvent@UObject@Unreal@RC@@QEAAXPEAVUFunction@23@PEAX@Z");
        // Outputs from FString-returning UE functions belong to Unreal's allocator.
        // This symbol is present in Inspection/UE4SS-exports.txt; do not use FreeHGlobal for them.
        _free = Bind<Free>("?Free@FMemory@Unreal@RC@@SAXPEAX@Z");
        using var stream = typeof(NativeFacialBridge).Assembly.GetManifestResourceStream("OblivionVoice.NativeFacial.tsv")
            ?? throw new InvalidDataException("Embedded native diagnostic contract is missing.");
        using var reader = new StreamReader(stream);
        _contracts = NativeLayoutTable.Read(reader);
    }

    public nint DefaultObject(string path) => _scriptObjects.Find(path, _findPath);
    private nint FindObjectPath(string path) => _find(0, 0, path, false);

    public nint Object(nint instance, string name)
    {
        if (instance == 0) throw new InvalidOperationException("Missing native object.");
        nint property = _property(instance, name);
        if (property == 0) throw new MissingMemberException(name);
        int offset = Marshal.ReadInt32(_offset(property)), size = Marshal.ReadInt32(_elementSize(property));
        if (offset < 0 || offset > 1_048_576 || size != 8) throw new NotSupportedException($"Native property contract changed: {name}.");
        return Marshal.ReadIntPtr(instance, offset);
    }

    private NativeFunctionLayout Resolve(string name, nint function)
    {
        if (_resolved.TryGetValue((name, function), out var cached)) return cached;
        if (!_contracts.TryGetValue(name, out var contract)) throw new NotSupportedException($"Native function is not allowed: {name}.");
        var properties = new List<(uint Name, int Offset, int Size)>();
        for (nint property = Marshal.ReadIntPtr(_children(function)); property != 0; property = Marshal.ReadIntPtr(_next(property)))
        {
            if (properties.Count >= 64) throw new NotSupportedException("Unexpected native parameter chain.");
            properties.Add((_index(_name(property)), Marshal.ReadInt32(_offset(property)), Marshal.ReadInt32(_elementSize(property))));
        }
        int size = (ushort)Marshal.ReadInt16(_paramSize(function));
        if (size < 0 || size > 16384 || properties.Count != contract.Fields.Count)
            throw new NotSupportedException($"Native parameter contract changed: {name}.");
        var fields = new Dictionary<string, (int Offset, int Size)>(StringComparer.Ordinal);
        if (name == "Conv_StringToName")
        {
            // Only bootstrap: reflected FString followed by either supported FName width.
            var input = properties.Single(p => p.Offset == 0 && p.Size == 16);
            var output = properties.Single(p => p.Offset == 16 && p.Size is 8 or 12);
            fields.Add("InString", (input.Offset, input.Size));
            fields.Add("ReturnValue", (output.Offset, output.Size));
        }
        else foreach (string field in contract.Fields.Keys)
        {
            if (!_fieldNames.TryGetValue(field, out uint index))
            {
                var result = Call(DefaultObject("/Script/Engine.Default__KismetStringLibrary"), "Conv_StringToName", ("InString", field));
                byte[] bytes = result.Bytes();
                nint memory = Marshal.AllocHGlobal(bytes.Length);
                try { Marshal.Copy(bytes, 0, memory, bytes.Length); index = _index(memory); }
                finally { Marshal.FreeHGlobal(memory); }
                _fieldNames.Add(field, index);
            }
            var property = properties.Single(p => p.Name == index);
            fields.Add(field, (property.Offset, property.Size));
        }
        var resolved = new NativeFunctionLayout(size, fields);
        // FName is 12 bytes in the authoring editor, but 8 in the shipping game.
        // Both soft-path structs contain two FNames. Derive their widths from the
        // same reflected bootstrap instead of accepting mismatched layouts.
        int nameWidth = name == "Conv_StringToName" ? fields["ReturnValue"].Size : _nameWidth;
        ValidateLayout(name, resolved, nameWidth);
        if (name == "Conv_StringToName") _nameWidth = nameWidth;
        _resolved.Add((name, function), resolved);
        return resolved;
    }

    internal static void ValidateLayout(string name, NativeFunctionLayout layout, int nameWidth)
    {
        if (nameWidth is not (8 or 12)) throw new NotSupportedException("Unsupported native FName width.");
        if (layout.Size < 0 || layout.Size > 16384) throw new NotSupportedException($"Invalid native parameter size: {name}.");
        foreach (var (field, value) in layout.Fields)
        {
            if (value.Offset < 0 || value.Size < 1 || value.Offset > layout.Size || value.Size > layout.Size - value.Offset)
                throw new NotSupportedException($"Invalid native argument bounds: {name}.{field}.");
            int width = ExpectedWidth(name, field, nameWidth);
            if (value.Size != width)
                throw new NotSupportedException($"Native argument width changed: {name}.{field} (expected {width}, found {value.Size}, FName {nameWidth}).");
        }
    }

    internal static int ExpectedWidth(string function, string field, int nameWidth)
    {
        if (nameWidth is not (8 or 12)) throw new NotSupportedException("Unsupported native FName width.");
        if (field == "SlotNodeName") return nameWidth;
        if (function == "Conv_StringToName") return field == "InString" ? 16 : nameWidth;
        if (field == "ReturnValue") return function switch
        {
            "IsValid" or "IsAnyMontagePlaying" => 1,
            "GetAnimInstance" or "GetComponentByClass" or "GetCurrentActiveMontage" or "LoadAsset_Blocking" or "PlaySlotAnimationAsDynamicMontage" => 8,
            "MakeSoftObjectPath" => 2 * nameWidth + 16,
            "Conv_SoftObjPathToSoftObjRef" => 2 * nameWidth + 24,
            "GetPlayLength" => 4,
            "GetObjectName" or "GetPathName" => 16,
            _ => throw new NotSupportedException($"No native return contract for {function}.")
        };
        return field switch
        {
            "Object" or "ComponentClass" or "Asset" or "Montage" => function == "LoadAsset_Blocking" && field == "Asset" ? 2 * nameWidth + 24 : 8,
            "SoftObjectPath" => 2 * nameWidth + 16,
            "PathString" => 16,
            "BlendInTime" or "BlendOutTime" or "InPlayRate" or "LoopCount" or "BlendOutTriggerTime" or "InTimeToStartMontageAt" or "InBlendOutTime" or "NewPosition" => 4,
            _ => throw new NotSupportedException($"No native width contract for {function}.{field}.")
        };
    }

    public unsafe NativeFacialResult Call(nint instance, string name, params (string Name, object Value)[] arguments)
    {
        ValidateArguments(name, arguments);
        if (instance == 0) throw new InvalidOperationException($"Missing target for {name}.");
        nint function = _function(instance, name);
        if (function == 0) throw new MissingMethodException(name);
        var layout = Resolve(name, function);
        if ((ushort)Marshal.ReadInt16(_paramSize(function)) != layout.Size) throw new NotSupportedException($"Native function size changed: {name}.");
        var data = new byte[Math.Max(1, layout.Size)];
        List<nint>? allocations = null;
        try
        {
            foreach (var (fieldName, value) in arguments)
            {
                var field = layout.Fields[fieldName];
                if (NativeArguments.TryWrite(data.AsSpan(field.Offset, field.Size), value)) continue;
                byte[] bytes = value switch
                {
                    nint pointer => BitConverter.GetBytes(pointer.ToInt64()),
                    int number => BitConverter.GetBytes(number),
                    float number => BitConverter.GetBytes(number),
                    bool flag => [flag ? (byte)1 : (byte)0],
                    byte number => [number],
                    byte[] raw => raw,
                    nint[] pointers => PointerArray(pointers),
                    string text => StringBytes(text),
                    _ => throw new ArgumentException("Unsupported native diagnostic argument.")
                };
                if (bytes.Length != field.Size) throw new NotSupportedException($"Native argument ABI mismatch: {name}.{fieldName}.");
                bytes.CopyTo(data, field.Offset);
            }
            fixed (byte* memory = data) _event(instance, function, (nint)memory);
            return new(data, layout);
        }
        finally
        {
            if (allocations is not null) foreach (nint allocation in allocations) Marshal.FreeHGlobal(allocation);
        }
        byte[] PointerArray(nint[] pointers)
        {
            nint array = Marshal.AllocHGlobal(pointers.Length * 8); (allocations ??= new()).Add(array);
            for (int i = 0; i < pointers.Length; i++) Marshal.WriteIntPtr(array, i * 8, pointers[i]);
            return [.. BitConverter.GetBytes(array.ToInt64()), .. BitConverter.GetBytes(pointers.Length), .. BitConverter.GetBytes(pointers.Length)];
        }
        byte[] StringBytes(string text)
        {
            nint buffer = Marshal.StringToHGlobalUni(text); (allocations ??= new()).Add(buffer);
            return [.. BitConverter.GetBytes(buffer.ToInt64()), .. BitConverter.GetBytes(text.Length + 1), .. BitConverter.GetBytes(text.Length + 1)];
        }
    }

    // Preflight the release/sampling APIs before taking over a head slot. If an engine update
    // removes one of them, fail before creating a paused montage that could not be released.
    public void Prepare(nint instance, string name)
    {
        if (instance == 0) throw new InvalidOperationException($"Missing target for {name}.");
        nint function = _function(instance, name);
        if (function == 0) throw new MissingMethodException(name);
        Resolve(name, function);
    }

    internal static void ValidateArguments(string name, params (string Name, object Value)[] arguments)
    {
        if (name is "Montage_Pause" or "Montage_Stop" or "Montage_SetPosition")
        {
            var montage = arguments.Where(a => a.Name == "Montage").ToArray();
            if (montage.Length != 1 || montage[0].Value is not nint pointer || pointer == 0)
                throw new ArgumentException("A specific nonzero owned montage is required; wildcard montage calls are forbidden.");
        }
        if (name == "Montage_SetPosition")
        {
            var position = arguments.Where(a => a.Name == "NewPosition").ToArray();
            if (position.Length != 1 || position[0].Value is not float seconds || !float.IsFinite(seconds) || seconds < 0)
                throw new ArgumentException("A finite nonnegative montage sample position is required.");
        }
    }

    public string ObjectText(nint systemLibrary, string function, nint instance)
    {
        if (function is not ("GetObjectName" or "GetPathName")) throw new ArgumentException("Not a supported native string function.");
        byte[] raw = Call(systemLibrary, function, ("Object", instance)).Bytes();
        if (raw.Length != 16) throw new NotSupportedException("Native FString contract changed.");
        nint data = (nint)BitConverter.ToInt64(raw, 0);
        int length = BitConverter.ToInt32(raw, 8), capacity = BitConverter.ToInt32(raw, 12);
        try
        {
            if (length < 0 || length > 65536 || capacity < length || capacity > 1_048_576 || length > 0 && data == 0)
                throw new InvalidDataException("Invalid native diagnostic string.");
            return length <= 1 ? "" : Marshal.PtrToStringUni(data, length - 1) ?? "";
        }
        finally { if (data != 0) _free(data); }
    }
}

internal sealed class NativeFacialResult(byte[] data, NativeFunctionLayout layout)
{
    private ReadOnlySpan<byte> View(string field) { var p = layout.Fields[field]; return data.AsSpan(p.Offset, p.Size); }
    public byte[] Bytes(string field = "ReturnValue")
    {
        var property = layout.Fields[field];
        return data.AsSpan(property.Offset, property.Size).ToArray();
    }
    public bool Boolean(string field = "ReturnValue") => View(field) is [var value] ? value != 0 : throw new NotSupportedException("Expected native Boolean.");
    public nint Pointer(string field = "ReturnValue")
    {
        ReadOnlySpan<byte> raw = View(field);
        return raw.Length == 8 ? (nint)BitConverter.ToInt64(raw) : throw new NotSupportedException("Expected native object pointer.");
    }
    public double Float(string field)
    {
        ReadOnlySpan<byte> raw = View(field);
        double value = raw.Length == 4 ? BitConverter.ToSingle(raw) : throw new NotSupportedException("Expected native float.");
        return double.IsFinite(value) ? value : throw new InvalidDataException("Non-finite native float.");
    }
}
