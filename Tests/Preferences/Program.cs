using OblivionVoice.Client;
using OblivionVoice.Common;
using System.Text.Json;
int n=0;
void Check(bool ok){if(!ok)throw new Exception($"Check {n+1} failed");n++;}
void Reject(VoicePreferences p){try{p.Validate();}catch(ArgumentException){n++;return;}throw new Exception("Invalid preferences accepted");}
var normal=new VoicePreferences();normal.Validate();
short[] samples=[0,1000,-1000,32767,-32768];normal.ApplyGain(samples);Check(samples.SequenceEqual(new short[]{0,1000,-1000,32767,-32768}));
new VoicePreferences{InputGainDb=6}.ApplyGain(samples);Check(samples[1]==1995 && samples[2]==-1995);Check(samples[3]==32767 && samples[4]==-32768);
short[] quiet=[1000,-1000];new VoicePreferences{InputGainDb=-6}.ApplyGain(quiet);Check(quiet[0]==501 && quiet[1]==-501);
Reject(new(){InputGainDb=float.NaN});Reject(new(){InputGainDb=25});Reject(new(){InputGainDb=-25});Reject(new(){InputDeviceId=null!});Reject(new(){TransmitMode=(TransmitMode)9});
var selected=new VoicePreferences{InputDeviceId="stable-device-id",TransmitKey="V",InputGainDb=3,TransmitMode=TransmitMode.Toggle};
var json=JsonSerializer.Serialize(selected);Check(!json.Contains("SessionToken") && !json.Contains("Host"));Check(JsonSerializer.Deserialize<VoicePreferences>(json)==selected);
Check(normal.TransmitMode is null);
Check(JsonSerializer.Deserialize<VoicePreferences>("{\"InputGainDb\":0}")!.TransmitKey is null);
Reject(new(){TransmitKey=""});Reject(new(){TransmitKey=new string('X',33)});
Check(normal.MouthAnimationEnabled && normal.MouthAnimationStrength==.65f);
Check(JsonSerializer.Deserialize<VoicePreferences>("{\"InputGainDb\":0}")!.MouthAnimationEnabled);
Reject(new(){MouthAnimationStrength=float.NaN});Reject(new(){MouthAnimationStrength=-.1f});Reject(new(){MouthAnimationStrength=1.1f});
var mouth=normal with {MouthAnimationEnabled=false,MouthAnimationStrength=.4f};Check(JsonSerializer.Deserialize<VoicePreferences>(JsonSerializer.Serialize(mouth))==mouth);
Console.WriteLine($"All {n} voice preference checks passed.");
