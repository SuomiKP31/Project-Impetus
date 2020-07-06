using System;
using CjLib;
using UnityEngine;

namespace GpuParticlesWithColliders
{
    public class Main_inter : MonoBehaviour
    {
        //------------------------------- Property Block ---------------------------------------//
        public const int dim = 64;
        public ComputeShader m_shader; // Probably only one shader is needed for init/step
        public ComputeShader m_gridShader; // Another used to generate grids and update them for each frame.
        public Material m_material; // Material which attached by the shader we use for rendering.

        [Range(0.0f, 2.0f)]
        public float m_timeScale = 1.0f;

        [Range(5.0f, 20.0f)]
        public float m_gravity = 9.8f;

        [Range(0.2f, 1.0f)]
        public float m_sphereRadius = 0.8f;

        [Range(0.0f, 1.0f)]
        public float m_restitution = 0.3f;

        [Range(0.0f, 1.0f)]
        public float m_friction = 0.7f;

        // Still need a control ball to play around the box though
        public GameObject m_controlSpherePrefab;
        private GameObject m_controlSphere;
        private Vector3 m_cSpherePrevPos;
        private Vector4 m_cSphereVel;

        // Floor prefab - Generate a visible floor
        public GameObject m_floorPrefab;
        // Wall prefab - Invisible wall around the scene. It should be used to constraint the particles, so that they won't go out of our grid box.
        public GameObject m_wallPrefab;

        private const int kNumParticles = 2000;
        private const float minParticleScale = 0.2f; // Grid edge length should be the diameter of the smallest particle, so that there won't be more than 4 particles in each grid.
        private const float maxParticleScale = 0.25f; // Fortunately, the scale of a standard ball particle is equal to its diameter. So directly use minParticleScale as diameter should cause no problem.

        //Constraints and grid related variables

        private int[] m_gridDimension = { dim, dim, dim }; // I think it's ok to set three dimensions to a same number, but for flexibility...
        private Vector4[] m_constraint = new Vector4[6]; // Ceiling, right, forward, left, back, floor.
        private Vector3 m_gridLowCorner;
        private Vector3 m_gridHighCorner;
        private Vector3 m_gridCenter = new Vector3(0,0,0);

        private ComputeBuffer m_computeBuffer; // Used to store particle struct
        private ComputeBuffer m_gridBuffer; // Used to store grid information. Should reset each frame.
        //private ComputeBuffer m_particleIndexBuffer; // Used to store particles' index in the grid temporarily. Should reset each frame.
        private ComputeBuffer m_instanceArgsBuffer;

        private int m_csInitKernelId;
        private int m_csStepKernelId;
        

        private int m_gsLoadGridKernelId; 
        private int m_gsInitParIndexKernelId;
        private int m_gsAssignGridKernelId;

        private int m_csParticleBufferId;
        private int m_csGridBufferId;
        private int m_csScaleId;
        private int m_csSpeedId;
        private int m_csNumParticlesId;
        private int m_csTimeId;
        private int m_csDynamicsId;
        // Need not to play around lifetime this time. No respawning needed either.
        private int m_csASphereId;
        private int m_csASphereVelId;
        private int m_csWallGroupId; // We need to have a group of walls to constraint the location of balls.
        private int m_csDampId;

        private int m_gsLowCornerId;
        private int m_gsHighCornerId;
        private int m_gsGridSizeId;
        private int m_gsGridDimensionId;
        private Mesh m_mesh;
        private MaterialPropertyBlock m_materialProperties;

        //--------------------------------------- End of Property Block ------------------------------------//

        void OnEnable()
        {
            m_mesh = new Mesh();
            m_mesh = PrimitiveMeshFactory.SphereWireframe(20,20);

            m_controlSphere = Instantiate(m_controlSpherePrefab);
            m_cSpherePrevPos = Vector3.zero;

            int particleStride = sizeof(float) * 20 + sizeof(uint) * 3;
            m_computeBuffer = new ComputeBuffer(kNumParticles, particleStride);
            int gridSize = m_gridDimension[0] * m_gridDimension[1] * m_gridDimension[2];
            m_gridBuffer = new ComputeBuffer(gridSize, sizeof(uint) * 4);

            uint[] instanceArgs = new uint[] { 0, 0, 0, 0, 0 }; //5 indices: index count per instance, instance count, start index location, base vertex location, start instance location.
            m_instanceArgsBuffer = new ComputeBuffer(1, instanceArgs.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            instanceArgs[0] = (uint)m_mesh.GetIndexCount(0);
            instanceArgs[1] = (uint)kNumParticles;
            instanceArgs[2] = (uint)m_mesh.GetIndexStart(0);
            instanceArgs[3] = (uint)m_mesh.GetBaseVertex(0); //start instance location is always 0
            m_instanceArgsBuffer.SetData(instanceArgs);

            m_csInitKernelId = m_shader.FindKernel("Init");// Kernel used to initialize particle location. (in ParticleLogic.compute)
            m_csStepKernelId = m_shader.FindKernel("Step");// Kernel used to update particles per frame

            m_gsLoadGridKernelId = m_gridShader.FindKernel("LoadGrid");
            m_gsInitParIndexKernelId = m_gridShader.FindKernel("InitParIndex");
            m_gsAssignGridKernelId = m_gridShader.FindKernel("AssignGrid");

            // We'll need ids to manage uniforms inside the shaders.
            // Note how to get them... They share a global namespace! 
            m_csParticleBufferId = Shader.PropertyToID("particleBuffer");
            m_csGridBufferId = Shader.PropertyToID("gridBuffer");
            m_csScaleId = Shader.PropertyToID("scale");
            m_csSpeedId = Shader.PropertyToID("speed");
            m_csNumParticlesId = Shader.PropertyToID("numParticles");
            m_csTimeId = Shader.PropertyToID("time");
            m_csDynamicsId = Shader.PropertyToID("dynamics");
            m_csASphereId = Shader.PropertyToID("aSphere");
            m_csASphereVelId = Shader.PropertyToID("aSphereVel");
            m_csWallGroupId = Shader.PropertyToID("plane");
            m_csDampId = Shader.PropertyToID("damping");

            m_gsGridDimensionId = Shader.PropertyToID("gridDimension");
            m_gsGridSizeId = Shader.PropertyToID("gridSize");
            m_gsHighCornerId = Shader.PropertyToID("gridHighCorner");
            m_gsLowCornerId = Shader.PropertyToID("gridLowCorner");

            m_materialProperties = new MaterialPropertyBlock();

            InitConstraints(); //Setup grid corners and walls. Store them inside member variables for easy pass later.
            InitParticles(); // Init: Setup materials & shaders
        }

        void Update()
        {
            if (!isActiveAndEnabled)
                return;
            GenerateGrid(); // Generate grid for next frame
            UpdateParticles(); // Update position, rotation, etc.
            RenderParticles();
        }
        void LateUpdate()
        {
            m_cSpherePrevPos = m_controlSphere.transform.position;
            m_controlSphere.transform.localScale = new Vector3(2.0f * m_sphereRadius, 2.0f * m_sphereRadius, 2.0f * m_sphereRadius);
        }
        void OnDisable()
        {
            if (m_computeBuffer != null)
            {
                m_computeBuffer.Dispose();
                m_computeBuffer = null;
            }

            if (m_instanceArgsBuffer != null)
            {
                m_instanceArgsBuffer.Dispose();
                m_instanceArgsBuffer = null;
            }

            if (m_gridBuffer != null)
            {
                m_gridBuffer.Dispose();
                m_gridBuffer = null;
            }
        }
        // Set buffer to the graphic shader
        private void SetUpMaterial()
        {
            m_material.enableInstancing = true; //Enable GPU instancing
            m_material.SetBuffer(m_csParticleBufferId, m_computeBuffer); //We'll use pos in the compute buffer to render the particles
        }

        // Give some random bounds to the compute shader. More importantly, set the buffer and particle count for it
        private void SetUpShader()
        {
            m_shader.SetFloats(m_csScaleId, new float[] { minParticleScale, maxParticleScale }); // scale controls the radius of the particles. (min,max)
            m_shader.SetFloats(m_csSpeedId, new float[] { 1.0f, 1.5f, 1.0f, 6.0f }); 
            // speed controls the (initial)speed limit. (min linear,max linear, min angular, max angular) The angular speed is in radians, and the roation axis is randomly generated in the shader.
            m_shader.SetInt(m_csNumParticlesId, kNumParticles);
            m_shader.SetFloat(m_csDampId, 1.02f); // The damping factor should be larger than 1, why?
            //It's more efficient to do multiplying (than dividing) on GPU so I used a intuitive brute force approach in quat_damp. I directly multiply q.w (the real part of the quaternion) and then normalize it, that damps the 
            // quaternion for a little bit.
            m_shader.SetVector(m_gsLowCornerId, m_gridLowCorner);
            m_shader.SetVector(m_gsHighCornerId, m_gridHighCorner);
            m_shader.SetVectorArray(m_csWallGroupId,m_constraint);

            m_shader.SetBuffer(m_csInitKernelId, m_csParticleBufferId, m_computeBuffer);
            m_shader.SetBuffer(m_csStepKernelId, m_csParticleBufferId, m_computeBuffer);
            m_shader.SetBuffer(m_csStepKernelId, m_csGridBufferId, m_gridBuffer);

            m_gridShader.SetBuffer(m_gsLoadGridKernelId, m_csParticleBufferId, m_computeBuffer);
            m_gridShader.SetBuffer(m_gsLoadGridKernelId, m_csGridBufferId,m_gridBuffer);
            m_gridShader.SetInt(m_csNumParticlesId, kNumParticles);
            // You'll need to set buffer for all kernels (Init kernel does not need grid though)
        }


        void InitParticles()
        {
            // TODO
            SetUpMaterial();
            SetUpShader();

            m_shader.Dispatch(m_csInitKernelId, kNumParticles, 1, 1);
        }
        void InitConstraints()
        {
            // In this code you initialize floor, ceiling and walls.Please note that y is the up axis, not z.
            Vector3 gridCubeSize;
            gridCubeSize.x = m_gridDimension[0] * minParticleScale;
            gridCubeSize.y = m_gridDimension[1] * minParticleScale;
            gridCubeSize.z = m_gridDimension[2] * minParticleScale;
            m_gridLowCorner = new Vector3(-0.5f * gridCubeSize.x, -0.5f * gridCubeSize.y, -0.5f * gridCubeSize.z);
            m_gridLowCorner += m_gridCenter;
            m_gridHighCorner = new Vector3(0.5f * gridCubeSize.x, 0.5f * gridCubeSize.y, 0.5f * gridCubeSize.z);
            m_gridHighCorner += m_gridCenter;
            //Ceiling
            Vector3 refCeiling = new Vector3(0f, m_gridHighCorner.y, 0f);
            calculateD(ref m_constraint[0], Vector3.down, refCeiling);
            //Right Wall
            Vector3 refRight = new Vector3(m_gridHighCorner.x, 0f , 0f);
            calculateD(ref m_constraint[1], Vector3.left, refRight);
            //Forward Wall
            Vector3 refForward = new Vector3(0f, 0f, m_gridHighCorner.z);
            calculateD(ref m_constraint[2], Vector3.back, refForward);
            // Left Wall
            Vector3 refLeft = new Vector3(m_gridLowCorner.x, 0f, 0f);
            calculateD(ref m_constraint[3], Vector3.right, refLeft);
            // Back Wall
            Vector3 refBack = new Vector3(0f, 0f, m_gridLowCorner.z);
            calculateD(ref m_constraint[4], Vector3.forward, refBack);
            // Floor
            Vector3 refFloor = new Vector3(0f, m_gridLowCorner.y, 0f);
            calculateD(ref m_constraint[5], Vector3.up, refFloor);
            
            // calculate 4 parameters normal(A,B,C) and D for ceiling, 4 walls and floor. You must make sure the consistency between this function and GenerateGrid()
        }
        void UpdateParticles()
        {
            // TODO
            // CPU knows time, now give it to GPU
            m_shader.SetFloats(m_csTimeId, new float[] { Time.time, m_timeScale * Time.fixedDeltaTime });
            // Dynamic parameters can change(by the sliders), so update every frame.
            m_shader.SetFloats(m_csDynamicsId, new float[] { m_gravity, m_restitution, m_friction });

            // Although we have only one control sphere, an array is still needed to paste the memory into GPU
            Vector4[] aSphere = new Vector4[1];
            Vector4[] cSphereVel = new Vector4[1];

            //m_cSpherePrevPos is updated in last lateUpdate;
            aSphere[0] = m_controlSphere.transform.position; aSphere[0].w = m_sphereRadius; //aSphere stores the position and radius for the control sphere
            m_cSphereVel = (m_controlSphere.transform.position - m_cSpherePrevPos) / Time.fixedDeltaTime; //Velocity can be calculated in CPU since we have only one control spehere. 
            cSphereVel[0] = m_cSphereVel;
            // Pass those into GPU
            m_shader.SetVectorArray(m_csASphereId, aSphere);
            m_shader.SetVectorArray(m_csASphereVelId, cSphereVel);
            m_shader.Dispatch(m_csStepKernelId, kNumParticles, 1, 1);
        }

        void RenderParticles() {
            Graphics.DrawMeshInstancedIndirect(m_mesh, 0, m_material, new Bounds(Vector3.zero, 20.0f * Vector3.one), m_instanceArgsBuffer, 0, m_materialProperties, UnityEngine.Rendering.ShadowCastingMode.On);
        }
        void GenerateGrid()
        {
            // TODO
            m_gridShader.SetBuffer(m_gsLoadGridKernelId, m_csGridBufferId, m_gridBuffer);
            m_gridShader.Dispatch(m_gsLoadGridKernelId, dim, dim, 1);

            m_gridShader.SetBuffer(m_gsInitParIndexKernelId, m_csParticleBufferId, m_computeBuffer);
            m_gridShader.Dispatch(m_gsInitParIndexKernelId, kNumParticles, 1, 1);

            m_gridShader.SetBuffer(m_gsAssignGridKernelId, m_csParticleBufferId, m_computeBuffer);
            m_gridShader.SetBuffer(m_gsAssignGridKernelId, m_csGridBufferId, m_gridBuffer);
            m_gridShader.Dispatch(m_gsAssignGridKernelId, kNumParticles, 1, 1);

            //
            // For Debug
            //
            /*uint[] res;
            res = new uint[64*64*64 * 4];
            //res = new uint[40*40*40 * 4];
            m_gridBuffer.GetData(res);

            uint sum = 0;
            for (int i = 0; i < 64 * 64 * 64 * 4; ++i)
                sum += res[i];
            Debug.Log(res[234]);*/
            //Debug.Log(sum - 64*64*64*4*1200);

        }
        void calculateD(ref Vector4 floor, Vector3 normal, Vector3 refpoint)
        {
            //calculate params for plane. normal must be normalized. refpoint is any point on the plane.
            floor.x = normal.x;
            floor.y = normal.y;
            floor.z = normal.z;
            floor.w = -Vector3.Dot(normal, refpoint); // D = -(Ax+By+Cz)
        }
    }
}
