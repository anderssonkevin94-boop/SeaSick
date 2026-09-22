// Bounded study-wave detail; CPU twin: GraphicWaveDetail.cs.
//
// **Every change in here is value-exact**, so the Burst twin still agrees and
// the divergence gate does not move. What it removes is recomputation: the
// first version took a fresh `cos` for a ridge's VALUE and then a fresh `sin`
// AND `cos` for that same ridge's SLOPE at the same argument, and it took
// another `sin`/`cos` pair for `q = phase - pi/2` when `sin phase` and
// `cos phase` were already sitting in registers two lines above. Seventeen
// sin/cos per wave became five `sincos`, and the three `sqrt`-and-divide pairs
// became three `rsqrt`. With sixteen of these evaluated per ocean pixel, on a
// GPU that runs transcendentals at quarter rate, that is the frame.

// The ridge profile as a function of its COSINE, so a caller holding cos(p)
// does not pay for another one. ArtRidgeC(cos p) == the old ArtRidge(p).
float ArtRidgeC(float x){return 1-2*acos(.985*x)/3.14159265;}
// d/dp of ArtRidgeC(cos p), given cos p and sin p. rsqrt rather than a sqrt
// and a divide; same value.
float ArtRidgeSlopeCS(float c,float s){return -2*.985*s*rsqrt(max(1-.970225*c*c,.0001))/3.14159265;}

// Kept for callers that only have the angle.
float ArtRidge(float p){return ArtRidgeC(cos(p));}
float ArtRidgeSlope(float p){float s,c;sincos(p,s,c);return ArtRidgeSlopeCS(c,s);}

 // Shared displacement and derivative: colored faces and foam describe the
 // very same peaked waves that change the mesh silhouette.
 void ArtWave(float2 p,float2 d,float wavelength,float amp,float offset,inout float h,inout float2 gradient,inout float foam){
  d=normalize(d);float2 crossDir=float2(-d.y,d.x);float across=dot(p,crossDir);
  float k=6.2831853/wavelength;
  float omega=sqrt(9.81*k);
  float along=dot(p,d);
  // Two travelling envelopes shorten the crests into groups. Their spatial
  // derivatives are included, so the painted normal matches the geometry.
  float group=along-omega/k*.47*_Ocean_ShorewardTime;
  float u=across*.105+group*.13+offset;
  float v=across*.217-group*.07+offset*2.3;
  float su,cu,sv,cv;sincos(u,su,cu);sincos(v,sv,cv);
  float packet=1-.75*(.38-.22*su-.16*sv);
  float2 packetGradient=.75*(.22*cu*(.105*crossDir+.13*d)
                      +.16*cv*(.217*crossDir-.07*d));
  // Each bend term and its slope share one argument, so one sincos serves
  // both instead of a cos for the value and a sin and a cos for the slope.
  float a0=across*.15+offset,a1=across*.31+along*.045+offset*3;
  float s0,c0,s1,c1;sincos(a0,s0,c0);sincos(a1,s1,c1);
  float bend=.75*ArtRidgeC(c0)+.25*ArtRidgeC(c1);
  float phase=k*along+bend-omega*_Ocean_ShorewardTime+offset;
  float2 phaseGradient=k*d+.1125*ArtRidgeSlopeCS(c0,s0)*crossDir
      +.25*ArtRidgeSlopeCS(c1,s1)*(.31*crossDir+.045*d);
  float s,c;sincos(phase,s,c);
  // q = phase - pi/2, so cos q == sin phase and sin q == -cos phase exactly.
  // profile = ArtRidge(q) + .24 sin q  and  profileSlope = ArtRidgeSlope(q) +
  // .24 cos q, both straight out of the pair already in hand.
  float profile=ArtRidgeC(s)-.24*c;
  float profileSlope=ArtRidgeSlopeCS(s,-c)+.24*s;
  h+=amp*packet*profile;
  gradient+=amp*(packet*profileSlope*phaseGradient+profile*packetGradient);
 }
 void ArtSurface(float2 p,out float h,out float2 gradient,out float foam){
  h=0;gradient=0;foam=0;
  ArtWave(p,float2(.20,1),32,1.15,0,h,gradient,foam);
  ArtWave(p,float2(-.18,1),14.5,.35,2.1,h,gradient,foam);
  ArtWave(p,float2(.45,1),5.4,.085,4.3,h,gradient,foam);
  ArtWave(p,float2(-.55,1),2.6,.012,1.7,h,gradient,foam);
  h*=.70;gradient*=.70;
}

// Local phase velocity points normal to each bent crest. Weight the larger
// waves near their peaks so crossings change direction smoothly.
float2 ArtWaveTravel(float2 p,float2 direction,float wavelength,float amplitude,float offset){
 float2 d=normalize(direction),crossDir=float2(-d.y,d.x);
 float across=dot(p,crossDir),along=dot(p,d),k=6.2831853/wavelength;
 float omega=sqrt(9.81*k);
 float a0=across*.15+offset,a1=across*.31+along*.045+offset*3;
 float s0,c0,s1,c1;sincos(a0,s0,c0);sincos(a1,s1,c1);
 float bend=.75*ArtRidgeC(c0)+.25*ArtRidgeC(c1);
 float phase=k*along+bend-omega*_Ocean_ShorewardTime+offset;
 float2 phaseGradient=k*d+.1125*ArtRidgeSlopeCS(c0,s0)*crossDir
  +.25*ArtRidgeSlopeCS(c1,s1)*(.31*crossDir+.045*d);
 float peakWeight=.20+.80*smoothstep(.30,.95,sin(phase));
 return amplitude*peakWeight*omega*phaseGradient/max(dot(phaseGradient,phaseGradient),.001);
}
float2 ArtTravelDirection(float2 p){
 float2 velocity=ArtWaveTravel(p,float2(.20,1),32,1.15,0)
  +ArtWaveTravel(p,float2(-.18,1),14.5,.35,2.1)
  +ArtWaveTravel(p,float2(.45,1),5.4,.085,4.3);
 return normalize(velocity);
}
