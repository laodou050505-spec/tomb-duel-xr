using System.Collections;
using UnityEngine;

namespace Guandan.Scene
{
    // One bounded celebration in the authored room. No camera shake, fullscreen flash,
    // colliders, or particles across the central result text / restart target.
    public sealed class TreasureCelebration : MonoBehaviour
    {
        private GameObject effectRoot;
        private Material[] ribbonMaterials;
        private Material bronzeMaterial;
        private Mesh ribbonMesh;
        private Coroutine cleanup;
        public bool IsPlaying => effectRoot != null && effectRoot.activeSelf;

        public void Play(Camera camera, Transform parent)
        {
            Clear();
            if (camera == null) return;
            effectRoot = new GameObject("VictoryConfetti · 双侧礼花");
            effectRoot.transform.SetParent(parent, false);
            var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, forward);
            var center = camera.transform.position + forward * 4.5f;
            center.y = Mathf.Max(0.45f, camera.transform.position.y - 1.5f);
            var palette = new[] { new Color(1f,0.76f,0.12f), new Color(0.15f,0.87f,0.70f),
                new Color(0.95f,0.20f,0.19f), new Color(1f,0.94f,0.64f) };
            ribbonMaterials = new Material[palette.Length];
            for(var i=0;i<palette.Length;i++)
            {
                ribbonMaterials[i] = new Material(Resources.Load<Shader>("GuandanUI/ConfettiRibbon"));
                ribbonMaterials[i].SetColor("_Color",palette[i]);
            }
            bronzeMaterial = new Material(Shader.Find("Standard"));
            bronzeMaterial.color = new Color(0.48f, 0.25f, 0.065f);
            bronzeMaterial.SetFloat("_Metallic", 0.75f); bronzeMaterial.SetFloat("_Glossiness", 0.55f);
            ribbonMesh = MakeRibbon();
            for (var side = -1; side <= 1; side += 2)
                MakeCannon(center + right * (side * 2.8f), (-right * side * 0.36f + Vector3.up).normalized, side);
            cleanup = StartCoroutine(CleanupAfter());
        }

        private void MakeCannon(Vector3 position, Vector3 direction, int side)
        {
            var cannon = new GameObject(side < 0 ? "ConfettiCannonLeft" : "ConfettiCannonRight");
            cannon.transform.SetParent(effectRoot.transform, false);
            cannon.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
            var tube = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tube.name = "Bronze celebration tube"; tube.transform.SetParent(cannon.transform, false);
            tube.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            tube.transform.localScale = new Vector3(0.19f, 0.26f, 0.19f);
            var collider = tube.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
            tube.GetComponent<Renderer>().sharedMaterial = bronzeMaterial;
            for(var colorIndex=0;colorIndex<ribbonMaterials.Length;colorIndex++)
                MakeRibbonBurst(cannon.transform,colorIndex);
        }

        private void MakeRibbonBurst(Transform cannon, int colorIndex)
        {
            var nozzle = new GameObject("RibbonBurst_"+colorIndex); nozzle.transform.SetParent(cannon, false);
            nozzle.transform.localPosition = Vector3.forward * 0.28f;
            var particles = nozzle.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = false; main.duration = 1.4f; main.startLifetime = new ParticleSystem.MinMaxCurve(3.2f, 4.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.2f, 5.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.40f);
            main.gravityModifier = 0.40f; main.maxParticles = 54;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            var emission = particles.emission; emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f,23), new ParticleSystem.Burst(0.5f,17), new ParticleSystem.Burst(1f,11) });
            var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 15f; shape.radius = 0.055f;
            var spin = particles.rotationOverLifetime; spin.enabled = true; spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-3f,3f); spin.y = new ParticleSystem.MinMaxCurve(-2f,2f); spin.z = new ParticleSystem.MinMaxCurve(-4f,4f);
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh; renderer.mesh = ribbonMesh;
            // Four shared solid materials keep the colors identical on Metal and PICO;
            // no platform-dependent particle vertex-color packing is consumed.
            renderer.enableGPUInstancing = false;
            renderer.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream>
                { ParticleSystemVertexStream.Position });
            renderer.sharedMaterial = ribbonMaterials[colorIndex]; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            particles.Play();
        }

        private static Mesh MakeRibbon()
        {
            const int segments = 8;
            var vertices = new Vector3[(segments+1)*2]; var triangles = new int[segments*6];
            var colors = new Color[vertices.Length];
            for (var i=0; i<=segments; i++)
            {
                var t=i/(float)segments;
                var bend=Mathf.Sin(t*Mathf.PI*2f)*0.10f;
                vertices[i*2]=new Vector3(-0.11f,(t-0.5f)*1.45f,bend);
                vertices[i*2+1]=new Vector3(0.11f,(t-0.5f)*1.45f,bend);
                colors[i*2]=colors[i*2+1]=Color.white;
                if(i==segments) continue;
                var k=i*6; var v=i*2;
                triangles[k]=v; triangles[k+1]=v+2; triangles[k+2]=v+1;
                triangles[k+3]=v+1; triangles[k+4]=v+2; triangles[k+5]=v+3;
            }
            var mesh=new Mesh { name="Curled silk confetti", vertices=vertices, triangles=triangles, colors=colors };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        private IEnumerator CleanupAfter()
        {
            yield return new WaitForSeconds(3f);
            for(var t=0f;t<2.6f;t+=Time.deltaTime)
            {
                foreach(var material in ribbonMaterials)
                { var color=material.GetColor("_Color"); color.a=1f-t/2.6f; material.SetColor("_Color",color); }
                yield return null;
            }
            cleanup=null; Clear();
        }
        public void Clear()
        {
            if(cleanup!=null) StopCoroutine(cleanup); cleanup=null;
            if(effectRoot!=null) { effectRoot.SetActive(false); Destroy(effectRoot); } effectRoot=null;
            if(ribbonMaterials!=null) foreach(var material in ribbonMaterials) if(material!=null) Destroy(material);
            ribbonMaterials=null;
            if(bronzeMaterial!=null) Destroy(bronzeMaterial); bronzeMaterial=null;
            if(ribbonMesh!=null) Destroy(ribbonMesh); ribbonMesh=null;
        }
        private void OnDestroy() => Clear();
    }
}
