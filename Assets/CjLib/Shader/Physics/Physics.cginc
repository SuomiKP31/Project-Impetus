/******************************************************************************/
/*
  Project - Unity CJ Lib
            https://github.com/TheAllenChou/unity-cj-lib
  
  Author  - Ming-Lun "Allen" Chou
  Web     - http://AllenChou.net
  Twitter - @TheAllenChou
*/
/******************************************************************************/

#ifndef CJ_LIB_PHYSICS
#define CJ_LIB_PHYSICS


#include "../Math/Math.cginc"
#include "../Math/Quaternion.cginc"

// common
//-----------------------------------------------------------------------------

struct CollisionResult
{
  float3 position;
  float3 velocity;
  float4 angularVelocity;
};

inline CollisionResult resolveCollision(float3 pos, float3 norm, float3 vel, float penetration, float restitution, float friction)
{
  float d = dot(vel, norm);   // projected relative speed onto contact normal
  float f = -d / length(vel); // ratio of relative speed along contact normal
  float3 velN = d * norm;     // normal relative velocity
  float3 velT = vel - velN;   // tangential relative velocity
  float3 velResolution = -(1.0 + restitution) * velN - friction * f * velT;

  CollisionResult res;
  res.position = pos + penetration * norm;
  res.velocity = vel + step(kEpsilon, penetration) * velResolution;
  return res;
}

inline CollisionResult resolveCollisionAngular(float4 pos, float3 norm, float3 vel, float4 avel, float penetration, float restitution, float friction)
{
  //Solve linear 
  float d = dot(vel, norm);   // projected relative speed onto contact normal
  float f = -d / length(vel); // ratio of relative speed along contact normal (this assumes contact normal is against inbound velocity)
  float3 velN = d * norm;     // normal relative velocity
  float3 velT = vel - velN;   // tangential relative velocity
  float3 frictionVel = - friction * f * velT;
  float3 velResolution = -(1.0 + restitution) * velN + frictionVel;
  // Solve Angular
  float3 relativePos = -norm * (pos.w - penetration); // norm is normalized. 
  float3 torque = cross(frictionVel,relativePos); // We don't work with mass so use frictionVel to represent the friction force.
  float4 angResolution = quat_axis_angle(torque,length(torque)); //torque is the axis. Angle represents how much the particle will turn each tick in radians.
  
  if (step(kEpsilon, penetration)) {
    angResolution = slerp(avel,angResolution, 0.8);
    // if (length(angResolution < 0.00001))
    //   angResolution = (0,0,0,0);
  } else {
    angResolution = avel;
  }
  CollisionResult res;
  res.position = pos.xyz + penetration * norm;
  res.velocity = vel + step(kEpsilon, penetration) * velResolution;
  
  //if (length(vel) < 0.3 && step(kEpsilon, penetration))
  //  res.velocity = (0,0,0);
  
  res.angularVelocity = angResolution;
  return res;
}
//-----------------------------------------------------------------------------
// end: common


// VS plane
//-----------------------------------------------------------------------------

inline CollisionResult pointVsPlane(float3 p, float4 plane, float3 vel, float restitution, float friction)
{
  float penetration = max(0.0, -dot(float4(p, 1.0), plane));
  float3 norm = plane.xyz;
  return resolveCollision(p, norm, vel, penetration, restitution, friction);
}

inline CollisionResult sphereVsPlane(float4 s, float4 plane, float3 vel, float restitution, float friction)
{
  float penetration = max(0.0, s.w - dot(float4(s.xyz, 1.0), plane));
  float3 norm = plane.xyz;
  return resolveCollision(s.xyz, norm, vel, penetration, restitution, friction);
}

inline CollisionResult sphereVsPlaneAngular(float4 s, float4 plane, float3 vel, float4 avel, float restitution, float friction)
{
  float penetration = max(0.0, s.w - dot(float4(s.xyz, 1.0), plane));
  float3 norm = plane.xyz;
  return resolveCollisionAngular(s, norm, vel, avel, penetration, restitution, friction);
}
//-----------------------------------------------------------------------------
// end: VS plane


// VS sphere
//-----------------------------------------------------------------------------

inline CollisionResult pointVsSphere(float3 p, float4 sphere, float3 vel, float restitution, float friction)
{
  float3 centerDiff = p - sphere.xyz;
  float centerDiffLen = length(centerDiff);
  float penetration = max(0.0, sphere.w - centerDiffLen);
  float3 norm = centerDiff / centerDiffLen;
  return resolveCollision(p, norm, vel, penetration, restitution, friction);
}

inline CollisionResult sphereVsSphere(float4 s, float4 sphere, float3 vel, float restitution, float friction)
{
  float3 centerDiff = s.xyz - sphere.xyz;
  float centerDiffLen = length(centerDiff);
  float penetration = max(0.0, s.w + sphere.w - centerDiffLen);
  float3 norm = centerDiff / centerDiffLen;
  return resolveCollision(s.xyz, norm, vel, penetration, restitution, friction);
}
inline CollisionResult sphereVsSphereAngular(float4 s, float4 sphere, float3 vel, float4 avel, float restitution, float friction)
{
  float3 centerDiff = s.xyz - sphere.xyz;
  float centerDiffLen = length(centerDiff);
  float penetration = max(0.0, s.w + sphere.w - centerDiffLen);
  float3 norm = centerDiff / centerDiffLen;
  return resolveCollisionAngular(s, norm, vel, avel, penetration, restitution, friction);
}
//-----------------------------------------------------------------------------
// end: VS sphere

#endif
