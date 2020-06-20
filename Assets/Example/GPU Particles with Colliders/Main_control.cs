/******************************************************************************/
/*
  Based on CJ-Lib GPU Particle example
  I'm just trying to prepare for the future work in Project Impetus
*/
/******************************************************************************/


using UnityEngine;

using CjLib;

namespace GpuParticlesWithColliders
{
    public class Main_control : MonoBehaviour
    {
        public ComputeShader m_shader;
        public Material m_material;

        private ComputeShader m_prevShader;
        private Material m_prevMaterial;

        [Range(0.0f, 2.0f)]
        public float m_timeScale = 1.0f;

        [Range(5.0f, 20.0f)]
        public float m_gravity = 9.8f;

        [Range(0.2f, 1.0f)]
        public float m_sphereRadius = 0.8f;

        [Range(-0.1f, 0.1f)]
        public float m_sphereSpeed = 0.05f;

        [Range(-1.0f, 1.0f)]
        public float m_floorTilt = 0.0f;

        [Range(-5.0f, 0.0f)]
        public float m_floorHeight = -2.0f;

        [Range(0.0f, 1.0f)]
        public float m_restitution = 0.3f;

        [Range(0.0f, 1.0f)]
        public float m_friction = 0.7f;

        public GameObject m_floorPrefab;
        public GameObject m_controlSpherePrefab;

        private GameObject m_controlSphere;
        private Vector3 m_cSpherePrevPos;
        private Vector4 m_cSphereVel;


        private GameObject m_floor;

        private const int kNumParticles = 1000;

        private ComputeBuffer m_computeBuffer;
        private ComputeBuffer m_instanceArgsBuffer;

        private Mesh m_mesh;
        private MaterialPropertyBlock m_materialProperties;

        private int m_csInitKernelId;
        private int m_csStepKernelId;

        private int m_csParticleBufferId;
        private int m_csScaleId;
        private int m_csSpeedId;
        private int m_csLifetimeId;
        private int m_csNumParticlesId;
        private int m_csTimeId;
        private int m_csDynamics;
        private int m_csASphere;
        private int m_csASphereVel;
        private int m_csPlane;

        void OnEnable()
        {
            m_mesh = new Mesh();
            m_mesh = PrimitiveMeshFactory.BoxFlatShaded();

            m_controlSphere = Instantiate(m_controlSpherePrefab);
            m_cSpherePrevPos = Vector3.zero;

            m_floor = Instantiate(m_floorPrefab);

            int particleStride = sizeof(float) * 24;
            m_computeBuffer = new ComputeBuffer(kNumParticles, particleStride);

            uint[] instanceArgs = new uint[] { 0, 0, 0, 0, 0 }; //5 indices: index count per instance, instance count, start index location, base vertex location, start instance location.
            m_instanceArgsBuffer = new ComputeBuffer(1, instanceArgs.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            instanceArgs[0] = (uint)m_mesh.GetIndexCount(0);
            instanceArgs[1] = (uint)kNumParticles;
            instanceArgs[2] = (uint)m_mesh.GetIndexStart(0);
            instanceArgs[3] = (uint)m_mesh.GetBaseVertex(0); //start instance location is always 0
            m_instanceArgsBuffer.SetData(instanceArgs);

            m_csInitKernelId = m_shader.FindKernel("Init");// Kernel used to initialize particle location. (in ParticleLogic.compute)
            m_csStepKernelId = m_shader.FindKernel("Step");// Kernel used to update particles per frame

            // We'll need ids to manage uniforms inside the shaders.
            // Note how to get them... They share a global namespace! 
            m_csParticleBufferId = Shader.PropertyToID("particleBuffer");
            m_csScaleId = Shader.PropertyToID("scale");
            m_csSpeedId = Shader.PropertyToID("speed");
            m_csLifetimeId = Shader.PropertyToID("lifetime");
            m_csNumParticlesId = Shader.PropertyToID("numParticles");
            m_csTimeId = Shader.PropertyToID("time");
            m_csDynamics = Shader.PropertyToID("dynamics");
            m_csASphere = Shader.PropertyToID("aSphere");
            m_csASphereVel = Shader.PropertyToID("aSphereVel");
            m_csPlane = Shader.PropertyToID("plane");

            m_materialProperties = new MaterialPropertyBlock();

            InitParticles(); // Init: Setup materials & shaders
        }

        void Update()
        {
            if (!isActiveAndEnabled)
                return;

            UpdateParticles();
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
            m_shader.SetFloats(m_csScaleId, new float[] { 0.1f, 0.15f }); // scale controls the radius of the particles. (min,max)
            m_shader.SetFloats(m_csSpeedId, new float[] { 1.0f, 1.5f, 1.0f, 6.0f }); // speed controls the (initial)speed limit. (min linear,max linear, min angular, max angular)
            m_shader.SetFloats(m_csLifetimeId, new float[] { 0.1f, 3.0f, 3.0f, 0.1f }); // In this example particles have a random lifetimeBody and the fixed lifetime head/tail
            // head is the time the particle grow to normal size, body is the time the particle fall and interact with the scene, tail is the time the particle shrink and decay
            m_shader.SetInt(m_csNumParticlesId, kNumParticles);

            m_shader.SetBuffer(m_csInitKernelId, m_csParticleBufferId, m_computeBuffer);
            m_shader.SetBuffer(m_csStepKernelId, m_csParticleBufferId, m_computeBuffer);
            // You'll need to set buffer for both kernels
        }

        // Initialize Particles
        private void InitParticles()
        {
            SetUpMaterial();
            SetUpShader();

            m_shader.Dispatch(m_csInitKernelId, kNumParticles, 1, 1); // Run the init kernel, only once. The group count is ofcourse equal to the particle count
        }

        // Update particle position, lifetime, dynamics. Also responsible in respawn particles (details in ParticleLogic.compute)
        private void UpdateParticles()
        {
            // You need to set up all parameters for each dispatch call
            SetUpMaterial();
            SetUpShader();

            // CPU knows time, now give it to GPU
            m_shader.SetFloats(m_csTimeId, new float[] { Time.time, m_timeScale * Time.fixedDeltaTime });
            // Dynamic parameters can change(by the sliders), so update every frame.
            m_shader.SetFloats(m_csDynamics, new float[] { m_gravity, m_restitution, m_friction });

            // Although we have only one control sphere, an array is still needed to paste the memory into GPU
            Vector4[] aSphere = new Vector4[1];
            Vector4[] cSphereVel = new Vector4[1];

            //m_cSpherePrevPos is updated in last lateUpdate;
            aSphere[0] = m_controlSphere.transform.position; aSphere[0].w = m_sphereRadius; //aSphere stores the position and radius for the control sphere
            m_cSphereVel = (m_controlSphere.transform.position - m_cSpherePrevPos) / Time.fixedDeltaTime; //Velocity can be calculated in CPU since we have only one control spehere. 
            cSphereVel[0] = m_cSphereVel;
            // Pass those into GPU
            m_shader.SetVectorArray(m_csASphere, aSphere);
            m_shader.SetVectorArray(m_csASphereVel, cSphereVel);

            // Update the floor
            Quaternion floorRot = Quaternion.AngleAxis(20.0f * m_floorTilt, Vector3.left); //Floor rotation represented by Quaternion. Range +- 20 degree
            // Visual change on CPU
            m_floor.transform.position = new Vector3(0.0f, m_floorHeight, 0.0f);
            m_floor.transform.rotation = floorRot;
            //Physic change on GPU: The floor is assumed infinitely wide, represented as Ax + By + Cz + D.
            Vector3 floorNormal = floorRot * Vector3.up;
            float floorD = -floorNormal.y * m_floorHeight; // A,B,C is normal(x,y,z), calculate D.
            m_shader.SetVector(m_csPlane, new Vector4(floorNormal.x, floorNormal.y, floorNormal.z, floorD));

            m_shader.Dispatch(m_csStepKernelId, kNumParticles, 1, 1);
        }

        private void RenderParticles()
        {
            Graphics.DrawMeshInstancedIndirect(m_mesh, 0, m_material, new Bounds(Vector3.zero, 20.0f * Vector3.one), m_instanceArgsBuffer, 0, m_materialProperties, UnityEngine.Rendering.ShadowCastingMode.On);
        }
    }
}
