using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using SeaSick.Ocean;
namespace SeaSick.Dev {
public static class GraphicWaveParity {
[MenuItem("SeaSick/Art/Verify shared wave detail")]
public static void Run(){
 var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/_Project/Art/Shaders/Ocean/GraphicWaveParity.compute");
 using(var buffer=new ComputeBuffer(1024,16)){
 shader.SetFloat("_Ocean_ShorewardTime",12.345f);shader.SetBuffer(0,"Results",buffer);shader.Dispatch(0,16,1,1);
 var gpu=new Vector4[1024];buffer.GetData(gpu);float height=0,gradient=0,velocity=0;
 for(int i=0;i<1024;i++){
  float2 p=new float2(i%32*3.17f-45,i/32*2.13f-38);var cpu=GraphicWaveDetail.Evaluate(p,12.345f);
  height=math.max(height,math.abs(cpu.x-gpu[i].x));gradient=math.max(gradient,math.length(cpu.yz-new float2(gpu[i].y,gpu[i].z)));
  float v=(GraphicWaveDetail.Evaluate(p,12.346f).x-GraphicWaveDetail.Evaluate(p,12.344f).x)/.002f;
  velocity=math.max(velocity,math.abs(v-cpu.w));
 }
 if(height>.002f||gradient>.003f||velocity>.015f)throw new Exception($"Wave parity failed: h={height}, gradient={gradient}, velocity={velocity}");
 Directory.CreateDirectory("docs/art-direction/game-ocean-integration");File.WriteAllText("docs/art-direction/game-ocean-integration/wave-parity.txt",$"PASS 1024 CPU/GPU samples; maximum height error {height} m; gradient {gradient}; analytic velocity vs time difference {velocity} m/s. Does not measure complete FFT readback lag or clipmap interpolation.\n");
 }
}
}}
