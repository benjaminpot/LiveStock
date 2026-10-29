using TMPro;
using Oculus.Interaction;
using UnityEngine;

namespace LiveStock.Multiplayer
{
    public sealed class VRPokeMenuButton : MonoBehaviour
    {
        [SerializeField] VRPokeMenuAction action;
        [SerializeField] TMP_Text label;
        [SerializeField] InteractableUnityEventWrapper interactionEvents;
        [SerializeField] PokeInteractable pokeInteractable;

        VRPokeMenu menu;

        public VRPokeMenuAction Action => action;

        void Awake()
        {
            if (interactionEvents == null)
                interactionEvents = GetComponentInChildren<InteractableUnityEventWrapper>(true);
            if (pokeInteractable == null)
                pokeInteractable = GetComponentInChildren<PokeInteractable>(true);
            interactionEvents.WhenSelect.AddListener(Invoke);
        }

        void OnDestroy()
        {
            if (interactionEvents != null)
                interactionEvents.WhenSelect.RemoveListener(Invoke);
        }

        void Invoke() => menu?.Invoke(action);

        public void Bind(VRPokeMenu owner) => menu = owner;

        public void SetAvailable(bool available)
        {
            gameObject.SetActive(available);
            if (pokeInteractable != null)
                pokeInteractable.enabled = available;
        }

        public void Configure(VRPokeMenuAction buttonAction, TMP_Text buttonLabel,
            InteractableUnityEventWrapper wrapper, PokeInteractable interactable)
        {
            action = buttonAction;
            label = buttonLabel;
            interactionEvents = wrapper;
            pokeInteractable = interactable;
        }
    }
}
