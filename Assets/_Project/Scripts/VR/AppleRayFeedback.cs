using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

/// <summary>Shows local aim and press feedback for menu test apples and network apples.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RayInteractable))]
public sealed class AppleRayFeedback : MonoBehaviour
{
    [SerializeField, Tooltip("The apple mesh to tint, without changing its shared material.")]
    private Renderer appleRenderer;
    private RayInteractable interactable;
    private readonly HashSet<int> hoveringPointers = new();
    private MaterialPropertyBlock originalProperties, feedbackProperties;
    private int colorProperty;
    private float selectedUntil;
    public bool IsHovered => hoveringPointers.Count > 0;
    public int SelectionCount { get; private set; }

    void Awake()
    {
        interactable = GetComponent<RayInteractable>();
        if (appleRenderer == null) appleRenderer = GetComponentInChildren<MeshRenderer>(true);
        originalProperties = new MaterialPropertyBlock();
        feedbackProperties = new MaterialPropertyBlock();
        appleRenderer.GetPropertyBlock(originalProperties);
        colorProperty = Shader.PropertyToID(appleRenderer.sharedMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color");
    }

    void OnEnable() => interactable.WhenPointerEventRaised += HandlePointerEvent;

    void OnDisable()
    {
        interactable.WhenPointerEventRaised -= HandlePointerEvent;
        hoveringPointers.Clear();
        selectedUntil = 0;
        if (appleRenderer != null) appleRenderer.SetPropertyBlock(originalProperties);
    }

    void Update()
    {
        if (selectedUntil > 0 && Time.unscaledTime >= selectedUntil)
        {
            selectedUntil = 0;
            ApplyFeedback();
        }
    }

    /// <summary>Tracks each hand separately so one hand leaving does not clear the other's highlight.</summary>
    void HandlePointerEvent(PointerEvent evt)
    {
        switch (evt.Type)
        {
            case PointerEventType.Hover:
                hoveringPointers.Add(evt.Identifier);
                break;
            case PointerEventType.Unhover:
            case PointerEventType.Cancel:
                hoveringPointers.Remove(evt.Identifier);
                break;
            case PointerEventType.Select:
                SelectionCount++;
                selectedUntil = Time.unscaledTime + .25f;
                break;
            default:
                return;
        }
        ApplyFeedback();
    }

    /// <summary>Flashes cyan on a press, uses yellow for aim, then restores the authored appearance.</summary>
    void ApplyFeedback()
    {
        if (selectedUntil <= 0 && !IsHovered)
        {
            appleRenderer.SetPropertyBlock(originalProperties);
            return;
        }
        appleRenderer.GetPropertyBlock(feedbackProperties);
        // The original palette texture can hide a tint (red multiplied by yellow still looks red).
        feedbackProperties.SetTexture("_BaseMap", Texture2D.whiteTexture);
        feedbackProperties.SetColor(colorProperty, selectedUntil > 0 ? Color.cyan : Color.yellow);
        appleRenderer.SetPropertyBlock(feedbackProperties);
    }
}
