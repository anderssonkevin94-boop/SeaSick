using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using SeaSick.CameraRig;
using SeaSick.Ship;
namespace SeaSick.Dev
{
 public static class TurnCameraReview
 {
  public static string Check()
  {
   var o=new SailingTurnOrbit();
   void Run(float yaw,float speed,float seconds,bool active=true){for(int i=0;i<Mathf.RoundToInt(seconds*60);i++)o.Step(yaw,speed,1f/60,active);}
   void Require(bool ok,string why){if(!ok)throw new Exception(why);}
   Require(o.Angle==0&&o.Side==0,"must start centered");
   Run(-2,5,5);Require(o.Side==0&&Mathf.Abs(o.Angle)<.01f,"minor corrections moved center");
   for(int i=0;i<20;i++){Run(-15,5,.2f);Run(15,5,.2f);}Require(o.Side==0&&Mathf.Abs(o.Angle)<8,"alternating taps earned quarter");
   o.Reset();Run(-15,0,2);Require(o.Side==0,"stationary yaw switched side");
   Run(-6,5,5);Require(o.Side==0&&o.Angle<0&&o.Angle> -8,"gentle turn too large");
   Run(-15,5,5);Require(o.Side==-1&&o.Angle< -23,"left quarter failed");
   Run(0,5,2);Require(o.Side==-1,"returned to center too early");
   Run(0,5,6);Require(o.Side==0&&Mathf.Abs(o.Angle)<.2f,"straight sailing failed to center");
   Run(15,5,5);Require(o.Side==1&&o.Angle>23,"right quarter failed");
   Run(-15,5,1);Require(o.Side==0&&o.Angle>0,"reversal skipped center");
   bool rested=false;float last=o.Angle;
   for(int i=0;i<420;i++){o.Step(-15,5,1f/60,true);if(Mathf.Abs(o.Angle)<2.5f&&o.Side==0)rested=true;Require(Mathf.Abs(o.Angle-last)<.5f,"orbit moved too fast");last=o.Angle;}
   Require(rested&&o.Side==-1&&o.Angle< -22,"reversal failed center pause/opposite quarter");
   float held=o.Angle;Run(15,5,3,false);Require(o.Angle==held,"override moved orbit");
   o.Step(-15,5,0,true);Require(!float.IsNaN(o.Angle),"zero dt invalid");
   o.Reset();Require(o.Side==0&&o.Angle==0,"reset failed");
   return "PASS: center start, minor corrections, taps, stationary, gentle turns, both quarters, three-second return, reversal center pause, rate cap, override, pause, reset";
  }
  static double began;static int shot;static ShipMotor motor;static ChaseCamera rig;
  public static void Start()
  {
   SailingVisualReview.Stage();motor=UnityEngine.Object.FindFirstObjectByType<ShipMotor>();
   motor.GetComponent<HelmInput>().enabled=false;motor.GetComponent<AnchorController>().CastOff();
   motor.ThrottleOrder=.5f;rig=UnityEngine.Object.FindFirstObjectByType<ChaseCamera>();
   File.WriteAllText("art-staging/turn-camera/live.txt", "");
   began=EditorApplication.timeSinceStartup;shot=0;EditorApplication.update-=Tick;EditorApplication.update+=Tick;
  }
  static void Tick()
  {
   if(!EditorApplication.isPlaying||motor==null){EditorApplication.update-=Tick;return;}
   float t=(float)(EditorApplication.timeSinceStartup-began);
   motor.Rudder=t<8?-1:t<20?1:0;
   if((shot==0&&t>7.5f)||(shot==1&&t>19.5f)||(shot==2&&t>27))
   {
    string name=shot==0?"left":shot==1?"right":"straight";
    typeof(SailingVisualReview).GetMethod("Render",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)
     .Invoke(null,new object[]{Camera.main,1080,2340,"rear-quarter/turn-"+name});
    File.AppendAllText("art-staging/turn-camera/live.txt",name+" angle="+rig.SailingQuarterAngle+" speed="+motor.CurrentSpeed+"\n");shot++;
   }
   if(t>28){motor.Rudder=0;motor.GetComponent<HelmInput>().enabled=true;EditorApplication.update-=Tick;EditorApplication.isPaused=true;}
  }
 }
}
