/***********************************/
/*
Grid Struct Used in inter-particle collision detection
*/
/***********************************/
#ifndef GRID_STRUCT
#define GRID_STRUCT
struct Grid
{
// each grid cell will contain 4 possible particle indices.
// To prevent data races, we'll use InterlockedExchange to atomically write the value to only one of them for each dispatch call.
// We'll call AssignGrid 4 times each frame and try to fill the grids.

	uint4 PbIndex; 
};

struct ParticleIndex
{
	// Grid index will not change within frame. So we only calculate it once.
	// Then store it in a buffer which will be cleaned each frame. (Grid buffer shall be cleaned and re-initialized each frame as well)
	uint3 GridIndex;
	bool outBound;
};
#endif