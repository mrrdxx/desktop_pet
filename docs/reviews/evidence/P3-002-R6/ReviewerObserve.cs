using System;
using System.IO;
using System.Text.Json;
namespace DesktopPet.App;
public partial class MainWindow {
 private string _reviewLast="";
 private void ReviewerObserve(double nowMs) {
  var c=StateContext?.SocialCoordinator;var session=c?.CurrentSession;
  var key=$"{session?.SessionId}|{session?.Phase}|{CurrentAction}|{_isSocialActive}|{_socialOwnsCuriosity}|{IsDragging}|{IsPlayerPaused}";
  if(key==_reviewLast)return;_reviewLast=key;
  var record=new { utc=DateTime.UtcNow, nowMs, pet=StateContext?.PetId, personality=Personality.ToString(), phase=session?.Phase.ToString(), initiator=session?.InitiatorId,target=session?.TargetId,active=c?.IsSocialActive,action=CurrentAction,socialOwns=_socialOwnsCuriosity,dragging=IsDragging,paused=IsPlayerPaused,left=Left,top=Top };
  var dir=Environment.GetEnvironmentVariable("DESKTOPPET_VALIDATION_DIR")!;
  File.AppendAllText(Path.Combine(dir,"social-observe.jsonl"),JsonSerializer.Serialize(record)+Environment.NewLine);
 }
}
