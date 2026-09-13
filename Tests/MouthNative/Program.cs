using OblivionVoice.Client.Animation;

int checks = 0;
void Check(bool success, string message)
{
    if (!success) throw new Exception(message);
    checks++;
}
void Reject<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T) { checks++; return; }
    throw new Exception(message);
}
void Near(float actual, float expected, string message) => Check(Math.Abs(actual-expected)<.00001, message);

Check(NativeFacialDriver.AnimationPath == "/Game/OblivionVoice/Animations/A_VoiceJaw.A_VoiceJaw", "Driver must request the cooked normalized voice animation.");
Check(NativeFacialDriver.SkeletonPath == "/Game/Art/Character/Humanoid/SKEL_HumanoidHeadRig.SKEL_HumanoidHeadRig", "Driver contract must use the separate native head skeleton.");
Check(NativeFacialDriver.JawCurve == "CTRL_expressions_jawOpen", "Jaw channel must match installed native facial curves.");
Check(NativeFacialDriver.Slot == "DefaultSlot", "Only the native head skeleton's registered slot may be used.");
Near(NativeFacialDriver.SamplePosition(0,1),0,"Silence samples neutral.");
Near(NativeFacialDriver.SamplePosition(.5f,1),.225f,"Half speech level samples moderate jaw movement.");
Near(NativeFacialDriver.SamplePosition(1,1),.45f,"Full microphone speech stays below exaggerated jaw opening.");
Near(NativeFacialDriver.SamplePosition(100,1),.45f,"User strength cannot exceed the physical cap.");
Near(NativeFacialDriver.SamplePosition(-1,1),0,"Negative levels cannot run before the clip.");
foreach(float length in new[]{0f,.5f,1.5f,float.NaN,float.PositiveInfinity})
    Reject<ArgumentException>(()=>NativeFacialDriver.SamplePosition(.5f,length),"Invalid curve-asset duration accepted.");
Reject<ArgumentException>(()=>NativeFacialDriver.SamplePosition(float.NaN,1),"Invalid speech level accepted.");

var driver = new NativeFacialDriver();
Check(driver.LastError is null,"Constructing driver must not touch UE4SS or the game.");
Check(!driver.Apply(default,.5f),"Default player must stop before any native call.");
Check(!driver.Apply(new(),.5f),"Invalid player must stop before any native call.");
Check(!driver.Apply(new(){IsValid=true,InDialogue=true},.5f),"Dialogue must stop before taking a head montage.");
Check(!driver.Apply(new(){IsValid=true,IsDead=true},.5f),"Dead actor must not animate.");
Check(!driver.Apply(new(){IsValid=true,Hp=0},.5f),"Zero-health actor must not animate.");
Check(!driver.Apply(new(){IsValid=true},0),"Silence must not initialize native bridge.");
driver.Release(default);driver.Forget("gone-player");driver.ForgetAll();
Check(driver.LastError is null,"No-op release/forget must require no native runtime.");

using var stream = typeof(NativeFacialDriver).Assembly.GetManifestResourceStream("OblivionVoice.NativeFacial.tsv")!;
using var reader = new StreamReader(stream);
var contracts = NativeLayoutTable.Read(reader);
Check(contracts.Count==16,"All facial-native contracts are embedded.");
Check(!contracts.ContainsKey("Montage_StopGroupByName")&&!contracts.ContainsKey("StopSlotAnimation")&&!contracts.ContainsKey("SetAnimationMode")&&!contracts.ContainsKey("ClearMorphTargets"),
    "Bridge must not expose global montage stops, animation-mode replacement or global face resets.");
foreach(var entry in contracts)
foreach(var field in entry.Value.Fields)
{
    int expected=NativeFacialBridge.ExpectedWidth(entry.Key,field.Key,12);
    Check(expected==field.Value.Size,$"Declared editor field width does not match contract: {entry.Key}.{field.Key}.");
}
// Actual UE5 layouts: FSoftObjectPath is two FNames plus an FString; the soft
// reference adds an eight-byte weak pointer. Shipping/editor FNames are 8/12.
// These independent fixtures reproduce the shipping-game acquisition failure.
var shipping = NativeLayoutTable.Read(new StringReader("""
Conv_StringToName|24|InString:0:16|ReturnValue:16:8
MakeSoftObjectPath|48|PathString:0:16|ReturnValue:16:32
Conv_SoftObjPathToSoftObjRef|72|SoftObjectPath:0:32|ReturnValue:32:40
LoadAsset_Blocking|48|Asset:0:40|ReturnValue:40:8
PlaySlotAnimationAsDynamicMontage|48|Asset:0:8|SlotNodeName:8:8|BlendInTime:16:4|BlendOutTime:20:4|InPlayRate:24:4|LoopCount:28:4|BlendOutTriggerTime:32:4|InTimeToStartMontageAt:36:4|ReturnValue:40:8
"""));
foreach(var entry in shipping) { NativeFacialBridge.ValidateLayout(entry.Key,entry.Value,8); checks++; }
foreach(var entry in contracts) { NativeFacialBridge.ValidateLayout(entry.Key,entry.Value,12); checks++; }
foreach(string fn in shipping.Keys)
{
    Reject<NotSupportedException>(()=>NativeFacialBridge.ValidateLayout(fn,contracts[fn],8),"Editor-size arguments accepted with the shipping FName ABI.");
    Reject<NotSupportedException>(()=>NativeFacialBridge.ValidateLayout(fn,shipping[fn],12),"Shipping-size arguments accepted with the editor FName ABI.");
}
foreach(int invalidNameWidth in new[]{0,4,16,int.MaxValue})
    Reject<NotSupportedException>(()=>NativeFacialBridge.ValidateLayout("MakeSoftObjectPath",shipping["MakeSoftObjectPath"],invalidNameWidth),"Unsupported or uninitialized FName ABI accepted.");
foreach(var invalidLayout in new NativeFunctionLayout[]
{
    new(-1,new(){{"ReturnValue",(0,32)}}),
    new(16385,new(){{"ReturnValue",(0,32)}}),
    new(48,new(){{"ReturnValue",(-1,32)}}),
    new(48,new(){{"ReturnValue",(17,32)}}),
    new(48,new(){{"ReturnValue",(16,0)}}),
    new(48,new(){{"ReturnValue",(int.MaxValue,32)}}),
    new(48,new(){{"ReturnValue",(16,int.MaxValue)}})
})
    Reject<NotSupportedException>(()=>NativeFacialBridge.ValidateLayout("MakeSoftObjectPath",invalidLayout,8),"Native parameter bounds failure accepted.");
foreach(var (layouts, nameWidth) in new[]{(shipping,8),(contracts,12)})
{
    // Exercise the opaque return bytes passed through the loader chain. The
    // pointer's offset changes too; retaining the editor offset would read past
    // the shipping function's argument buffer.
    foreach(var (producer, consumer, input) in new[]{
        ("MakeSoftObjectPath","Conv_SoftObjPathToSoftObjRef","SoftObjectPath"),
        ("Conv_SoftObjPathToSoftObjRef","LoadAsset_Blocking","Asset")})
    {
        var layout=layouts[producer];
        var raw=Enumerable.Range(0,layout.Size).Select(i=>(byte)i).ToArray();
        var returned=new NativeFacialResult(raw,layout).Bytes();
        Check(returned.Length==layouts[consumer].Fields[input].Size,"Loader output must fit the next function's reflected input.");
        Check(returned.Length==NativeFacialBridge.ExpectedWidth(consumer,input,nameWidth),"Loader chain must retain the observed FName ABI.");
        Check(returned[0]==layout.Fields["ReturnValue"].Offset,"Opaque return must use its reflected offset.");
    }
    var load=layouts["LoadAsset_Blocking"];
    var loaded=new byte[load.Size];
    BitConverter.GetBytes(0x12345678L).CopyTo(loaded,load.Fields["ReturnValue"].Offset);
    Check(new NativeFacialResult(loaded,load).Pointer()==(nint)0x12345678,"Loaded animation pointer must use the runtime return offset.");
}
foreach(string malformed in new[]{"Bad|-1","Bad|8|Object:1:8","Bad|8|Object:0:-1","Bad|8|Object:0:8|Object:0:8","Bad|8\nBad|8"})
    Reject<InvalidDataException>(()=>NativeLayoutTable.Read(new StringReader(malformed)),"Invalid native metadata was accepted.");
foreach(string fn in new[]{"Montage_Pause","Montage_Stop","Montage_SetPosition"})
{
    Reject<ArgumentException>(()=>NativeFacialBridge.ValidateArguments(fn),"Missing montage became a global wildcard call.");
    Reject<ArgumentException>(()=>NativeFacialBridge.ValidateArguments(fn,("Montage",nint.Zero)),"Null montage became a global wildcard call.");
    Reject<ArgumentException>(()=>NativeFacialBridge.ValidateArguments(fn,("Montage",(nint)1),("Montage",(nint)2)),"Duplicate montage arguments were accepted.");
}
NativeFacialBridge.ValidateArguments("Montage_Stop",("Montage",(nint)123),("InBlendOutTime",.1f));checks++;
NativeFacialBridge.ValidateArguments("Montage_Pause",("Montage",(nint)123));checks++;
NativeFacialBridge.ValidateArguments("Montage_SetPosition",("Montage",(nint)123),("NewPosition",.225f));checks++;
foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,-.1f})
    Reject<ArgumentException>(()=>NativeFacialBridge.ValidateArguments("Montage_SetPosition",("Montage",(nint)123),("NewPosition",invalid)),"Invalid sample time accepted.");
Reject<NotSupportedException>(()=>new NativeFacialResult(new byte[4],new(4,new(){{"ReturnValue",(0,4)}})).Pointer(),"Changed native object-pointer width was accepted.");
Reject<InvalidDataException>(()=>new NativeFacialResult(BitConverter.GetBytes(float.NaN),new(4,new(){{"ReturnValue",(0,4)}})).Float("ReturnValue"),"Non-finite returned clip length was accepted.");
var retry = new NativeFailureCooldown();
retry.Record("alice","Head not ready",10);
Check(retry.TryGet("alice",10,out var reason)&&reason=="Head not ready","Native failure keeps its diagnostic during cooldown.");
Check(retry.TryGet("alice",11.999,out _),"Native failure prevents 30-Hz retry for the full two-second cooldown.");
Check(!retry.TryGet("bob",11,out _),"One unsupported actor must not block other speakers.");
Check(!retry.TryGet("alice",12,out _),"Failed native acquisition may retry after two seconds.");
retry.Record("alice","Head not ready",15);retry.Forget("alice");
Check(!retry.TryGet("alice",15.1,out _),"A respawned actor must not inherit the old actor's cooldown.");
retry.Record("alice","Head not ready",20);retry.Record("bob","Asset not ready",20);retry.Clear();
Check(!retry.TryGet("alice",20.1,out _)&&!retry.TryGet("bob",20.1,out _),"Disconnect clears all native retry state.");
var cache = new NativeScriptObjectCache();
int lookups = 0;
nint Find(string _) { lookups++; return (nint)123; }
for (int frame = 0; frame < 3000; frame++)
{
    cache.Find("/Script/Engine.Default__KismetSystemLibrary", Find);
    cache.Find("/Script/Altar.VHumanoidHeadComponent", Find);
}
Check(lookups == 2, "Stable native objects must not repeat global searches while speaking.");
cache.Find(NativeFacialDriver.AnimationPath, Find);
cache.Find(NativeFacialDriver.AnimationPath, Find);
Check(lookups == 4, "Collectable game assets must be re-resolved, not held as stale pointers.");
int retries = 0;
nint Late(string _) => ++retries == 1 ? 0 : (nint)456;
Check(cache.Find("/Script/Late.Class", Late) == 0, "Missing object remains unavailable.");
Check(cache.Find("/Script/Late.Class", Late) == (nint)456, "Missing object is retried when it becomes available.");
var packet = new byte[16];
Check(NativeArguments.TryWrite(packet.AsSpan(0,8), (nint)0x12345678), "Pointer packed directly.");
Check(NativeArguments.TryWrite(packet.AsSpan(8,4), .25f), "Float packed directly.");
Check(BitConverter.ToInt64(packet,0) == 0x12345678 && BitConverter.ToSingle(packet,8) == .25f, "Direct argument packing preserves native values and offsets.");
Reject<NotSupportedException>(() => NativeArguments.TryWrite(new byte[4], (nint)1), "Pointer truncation must be rejected.");
Reject<NotSupportedException>(() => NativeArguments.TryWrite(new byte[8], .25f), "Float width mismatch must be rejected.");
object boxedFloat = .25f;
for (int i=0; i<100; i++) NativeArguments.TryWrite(packet.AsSpan(8,4), boxedFloat);
long allocationStart = GC.GetAllocatedBytesForCurrentThread();
for (int i=0; i<10000; i++) NativeArguments.TryWrite(packet.AsSpan(8,4), boxedFloat);
long allocated = GC.GetAllocatedBytesForCurrentThread()-allocationStart;
Check(allocated == 0, "Steady-state primitive packing must not allocate temporary arrays.");
Console.WriteLine($"Performance checks: 6000 native-object requests used {lookups-2} global lookups before dynamic-asset tests; 10000 float writes allocated {allocated} bytes.");
Console.WriteLine($"PASS: {checks} mouth-native offline checks. No SDK or native game calls were made.");
