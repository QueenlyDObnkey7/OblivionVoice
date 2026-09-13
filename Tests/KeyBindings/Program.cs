using OblivionVoice.Client;
using OblivionVoice.Client.Input;
using OblivionMp.Sdk;
using ReadyM.Sdk.Common.Input;
using Microsoft.Extensions.Logging.Abstractions;
int checks=0;
void Check(bool b,string why){if(!b)throw new Exception(why);checks++;}
var input=new ReadyMKeyBindings(NullLogger.Instance);var runtime=new VoiceRuntime();var settings=new VoiceClientSettings();
input.Register(settings,runtime);SDK.Input.Callbacks[Key.N]();Check(runtime.Presses==1,"Default key transmits");
input.RegisterTransmit("V",runtime,settings);SDK.Input.Callbacks[Key.V]();Check(runtime.Presses==1,"Prepared unsaved key stays inactive");
runtime.EffectiveTransmitKey="V";SDK.Input.Callbacks[Key.N]();Check(runtime.Presses==1,"Old key stops transmitting after apply");
SDK.Input.Callbacks[Key.V]();Check(runtime.Presses==2,"New key transmits after apply");
input.RegisterTransmit("V",runtime,settings);Check(SDK.Input.Registrations[Key.V]==1,"Repeated apply does not duplicate callbacks");
SDK.Input.Allowed=false;SDK.Input.Callbacks[Key.V]();Check(runtime.Presses==2,"Menus block voice key");SDK.Input.Allowed=true;
runtime.EffectiveTransmitKey="N";SDK.Input.Callbacks[Key.V]();SDK.Input.Callbacks[Key.N]();Check(runtime.Presses==3,"Restored default deactivates previous choice");
Console.WriteLine($"All {checks} voice binding integration checks passed.");
namespace ReadyM.Sdk.Common.Input {public enum Key {N,V,F6,F7}}
namespace OblivionMp.Sdk {public static class SDK {public static class Input {public static bool Allowed=true;public static bool CanApplyInput()=>Allowed;public static Dictionary<Key,Action> Callbacks=[];public static Dictionary<Key,int> Registrations=[];public static void RegisterKeyBind(Key key,Action action){Callbacks[key]=action;Registrations[key]=Registrations.GetValueOrDefault(key)+1;}}}}
namespace OblivionVoice.Client {
 public class VoiceClientSettings {public string TransmitKey="N",CycleRangeKey="F6",ToggleMuteKey="F7";public bool DebugEnabled,DebugLogKeyEvents;}
 public class VoiceRuntime {public string EffectiveTransmitKey="N";public int Presses;public void OnTransmitKeyPressed()=>Presses++;public void CycleMode(){}public void ToggleMute(){}}
}
