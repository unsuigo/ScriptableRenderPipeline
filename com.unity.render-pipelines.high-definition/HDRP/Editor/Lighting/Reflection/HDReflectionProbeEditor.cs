using System.Collections.Generic;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering.HDPipeline;
using UnityEngine.Rendering;

namespace UnityEditor.Experimental.Rendering.HDPipeline
{
    [CustomEditorForRenderPipeline(typeof(ReflectionProbe), typeof(HDRenderPipelineAsset))]
    [CanEditMultipleObjects]
    partial class HDReflectionProbeEditor : HDProbeEditor
    {
        [MenuItem("CONTEXT/ReflectionProbe/Remove Component", false, 0)]
        static void RemoveReflectionProbe(MenuCommand menuCommand)
        {
            GameObject go = ((ReflectionProbe)menuCommand.context).gameObject;

            Assert.IsNotNull(go);

            Undo.SetCurrentGroupName("Remove HD Reflection Probe");
            Undo.DestroyObjectImmediate(go.GetComponent<ReflectionProbe>());
            Undo.DestroyObjectImmediate(go.GetComponent<HDAdditionalReflectionData>());
        }

        [MenuItem("CONTEXT/ReflectionProbe/Reset", false, 0)]
        static void ResetReflectionProbe(MenuCommand menuCommand)
        {
            GameObject go = ((ReflectionProbe)menuCommand.context).gameObject;

            Assert.IsNotNull(go);

            ReflectionProbe reflectionProbe = go.GetComponent<ReflectionProbe>();
            HDAdditionalReflectionData reflectionProbeAdditionalData = go.GetComponent<HDAdditionalReflectionData>();

            Assert.IsNotNull(reflectionProbe);
            Assert.IsNotNull(reflectionProbeAdditionalData);

            Undo.SetCurrentGroupName("Reset HD Reflection Probe");
            Undo.RecordObjects(new UnityEngine.Object[] { reflectionProbe, reflectionProbeAdditionalData }, "Reset HD Reflection Probe");
            reflectionProbe.Reset();
            // To avoid duplicating init code we copy default settings to Reset additional data
            // Note: we can't call this code inside the HDAdditionalReflectionData, thus why we don't wrap it in Reset() function
            if(HDUtils.s_DefaultHDAdditionalReflectionData.influenceVolume == null)
            {
                HDUtils.s_DefaultHDAdditionalReflectionData.Awake();
            }
            HDUtils.s_DefaultHDAdditionalReflectionData.CopyTo(reflectionProbeAdditionalData);
        }

        static Dictionary<ReflectionProbe, HDReflectionProbeEditor> s_ReflectionProbeEditors = new Dictionary<ReflectionProbe, HDReflectionProbeEditor>();

        internal override HDProbe GetTarget(Object editorTarget)
        {
            return (HDProbe)s_ReflectionProbeEditors[(ReflectionProbe)editorTarget].m_AdditionalDataSerializedObject.targetObject;
        }

        protected override void Draw(HDProbeUI s, SerializedHDProbe serialized, Editor owner)
        {
            HDReflectionProbeUI.Inspector.Draw(s, serialized, owner);
        }

        static HDReflectionProbeEditor GetEditorFor(ReflectionProbe p)
        {
            HDReflectionProbeEditor e;
            if (s_ReflectionProbeEditors.TryGetValue(p, out e)
                && e != null
                && !e.Equals(null)
                && ArrayUtility.IndexOf(e.targets, p) != -1)
                return e;

            return null;
        }
        
        SerializedObject m_AdditionalDataSerializedObject;
        internal HDReflectionProbeUI m_UIState = new HDReflectionProbeUI();

        int m_PositionHash = 0;

        public bool sceneViewEditing
        {
            get { return IsReflectionProbeEditMode(EditMode.editMode) && EditMode.IsOwner(this); }
        }
        
        protected override void OnEnable()
        {
            var additionalData = CoreEditorUtils.GetAdditionalData<HDAdditionalReflectionData>(targets);
            m_AdditionalDataSerializedObject = new SerializedObject(additionalData);
            m_SerializedHDProbe = new SerializedHDReflectionProbe(serializedObject, m_AdditionalDataSerializedObject);

            foreach (var t in targets)
            {
                var p = (ReflectionProbe)t;
                s_ReflectionProbeEditors[p] = this;
            }

            base.OnEnable();

            m_UIState.owner = this;
            m_UIState.Reset(m_SerializedHDProbe, Repaint);
            
            InitializeTargetProbe();
        }

        public static bool IsReflectionProbeEditMode(EditMode.SceneViewEditMode editMode)
        {
            return editMode == EditMode.SceneViewEditMode.ReflectionProbeBox || editMode == EditMode.SceneViewEditMode.Collider || editMode == EditMode.SceneViewEditMode.GridBox ||
                editMode == EditMode.SceneViewEditMode.ReflectionProbeOrigin;
        }

        void BakeRealtimeProbeIfPositionChanged(HDReflectionProbeUI s, SerializedHDReflectionProbe sp, Editor o)
        {
            if (Application.isPlaying
                || ((ReflectionProbeMode)sp.mode.intValue) != ReflectionProbeMode.Realtime)
            {
                m_PositionHash = 0;
                return;
            }

            var hash = 0;
            for (var i = 0; i < sp.serializedLegacyObject.targetObjects.Length; i++)
            {
                var p = (ReflectionProbe)sp.serializedLegacyObject.targetObjects[i];
                var tr = p.GetComponent<Transform>();
                hash ^= tr.position.GetHashCode();
            }

            if (hash != m_PositionHash)
            {
                m_PositionHash = hash;
                for (var i = 0; i < sp.serializedLegacyObject.targetObjects.Length; i++)
                {
                    var p = (ReflectionProbe)sp.serializedLegacyObject.targetObjects[i];
                    p.RenderProbe();
                }
            }
        }

        static void ApplyConstraintsOnTargets(HDReflectionProbeUI s, SerializedHDReflectionProbe sp, Editor o)
        {
            switch ((InfluenceShape)sp.influenceVolume.shape.enumValueIndex)
            {
                case InfluenceShape.Box:
                {
                    var maxBlendDistance = sp.influenceVolume.boxSize.vector3Value;
                    sp.target.influenceVolume.boxBlendDistancePositive = Vector3.Min(sp.target.influenceVolume.boxBlendDistancePositive, maxBlendDistance);
                    sp.target.influenceVolume.boxBlendDistanceNegative = Vector3.Min(sp.target.influenceVolume.boxBlendDistanceNegative, maxBlendDistance);
                    sp.target.influenceVolume.boxBlendNormalDistancePositive = Vector3.Min(sp.target.influenceVolume.boxBlendNormalDistancePositive, maxBlendDistance);
                    sp.target.influenceVolume.boxBlendNormalDistanceNegative = Vector3.Min(sp.target.influenceVolume.boxBlendNormalDistanceNegative, maxBlendDistance);
                    break;
                }
                case InfluenceShape.Sphere:
                {
                    var maxBlendDistance = Vector3.one * sp.influenceVolume.sphereRadius.floatValue;
                    sp.target.influenceVolume.boxBlendDistancePositive = Vector3.Min(sp.target.influenceVolume.boxBlendDistancePositive, maxBlendDistance);
                    sp.target.influenceVolume.boxBlendDistanceNegative = Vector3.Min(sp.target.influenceVolume.boxBlendDistanceNegative, maxBlendDistance);
                    sp.target.influenceVolume.boxBlendNormalDistancePositive = Vector3.Min(sp.target.influenceVolume.boxBlendNormalDistancePositive, maxBlendDistance);
                    sp.target.influenceVolume.boxBlendNormalDistanceNegative = Vector3.Min(sp.target.influenceVolume.boxBlendNormalDistanceNegative, maxBlendDistance);
                    break;
                }
            }
        }
    }
}
