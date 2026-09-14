var h=SeaSick.World.Island.TerrainHeight;
const int n=121;const float step=2f;const float ox=-120,oz=-55;
var ys=new float[n*n];var seen=new bool[n*n];var queue=new System.Collections.Generic.Queue<int>();
for(int z=0;z<n;z++)for(int x=0;x<n;x++)ys[z*n+x]=h(ox+x*step,oz+z*step);
int sx=UnityEngine.Mathf.RoundToInt((7-ox)/step),sz=UnityEngine.Mathf.RoundToInt((48-oz)/step);queue.Enqueue(sz*n+sx);seen[sz*n+sx]=true;
while(queue.Count>0){int i=queue.Dequeue(),x=i%n,z=i/n;foreach(var off in new[]{-1,1,-n,n}){int j=i+off;if(j<0||j>=ys.Length||System.Math.Abs(j%n-x)>1)continue;if(seen[j]||ys[j]<.5f||System.Math.Abs(ys[j]-ys[i])/step>.70f)continue;seen[j]=true;queue.Enqueue(j);}}
var w=UnityEngine.Object.FindObjectsByType<SeaSick.Terrain.SceneryWood>(UnityEngine.FindObjectsSortMode.None).First(w=>w.transform.parent.name=="Island_Home");int reachable=0;
for(int i=0;i<w.TreeCount;i++){var p=w.TreeAt(i).baseAt;int x=UnityEngine.Mathf.RoundToInt((p.x-ox)/step),z=UnityEngine.Mathf.RoundToInt((p.z-oz)/step);if(x>=0&&x<n&&z>=0&&z<n&&seen[z*n+x])reachable++;}
string report="Home land connected at slope <=0.70: "+seen.Count(x=>x)+" samples; trees reachable="+reachable+"/"+w.TreeCount+"; highest reached="+Enumerable.Range(0,ys.Length).Where(i=>seen[i]).Max(i=>ys[i]);
System.IO.File.AppendAllText("/Users/kevinandersson/Desktop/SeaSick/docs/art-direction/storybook-validation.txt",report+"\n");return report;
