using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed class MenuTextHoverManager : MonoBehaviour
{
    [SerializeField] private Text[] menuTexts;

    private const string HoverPrefix = "> ";

    private sealed class HoverBinding
    {
        public Text Label;
        public string OriginalText;
        public bool IsHovered;

        public EventTrigger.Entry EnterEntry;
        public EventTrigger.Entry ExitEntry;
        public UnityAction<BaseEventData> EnterCallback;
        public UnityAction<BaseEventData> ExitCallback;
    }

    private readonly List<HoverBinding> bindings = new List<HoverBinding>();
    private bool initialized;

    private void Awake()
    {
        Initialize();
    }

    private void OnEnable()
    {
        if (!initialized)
            Initialize();
    }

    private void Initialize()
    {
        if (menuTexts == null)
            return;

        initialized = true;

        for (int i = 0; i < menuTexts.Length; i++)
        {
            Text label = menuTexts[i];
            if (label == null)
                continue;

            Button button = label.GetComponentInParent<Button>();
            GameObject target = button != null ? button.gameObject : label.gameObject;

            HoverBinding binding = new HoverBinding
            {
                Label = label,
                OriginalText = label.text
            };

            if (button == null && !label.raycastTarget)
                label.raycastTarget = true;

            EventTrigger trigger = target.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = target.AddComponent<EventTrigger>();

            binding.EnterCallback = _ =>
            {
                if (this != null && isActiveAndEnabled)
                    SetHovered(binding, true);
            };
            binding.ExitCallback = _ =>
            {
                if (this != null && isActiveAndEnabled)
                    SetHovered(binding, false);
            };

            binding.EnterEntry = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerEnter
            };
            binding.EnterEntry.callback.AddListener(binding.EnterCallback);

            binding.ExitEntry = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerExit
            };
            binding.ExitEntry.callback.AddListener(binding.ExitCallback);

            trigger.triggers.Add(binding.EnterEntry);
            trigger.triggers.Add(binding.ExitEntry);
            bindings.Add(binding);
        }
    }

    private static void SetHovered(HoverBinding binding, bool hovered)
    {

        if (binding.Label == null || binding.IsHovered == hovered)
            return;

        binding.IsHovered = hovered;
        binding.Label.text = hovered
            ? HoverPrefix + binding.OriginalText
            : binding.OriginalText;
    }

    private void OnDisable()
    {

        for (int i = 0; i < bindings.Count; i++)
        {
            HoverBinding binding = bindings[i];
            binding.IsHovered = false;
            if (binding.Label != null)
                binding.Label.text = binding.OriginalText;
        }
    }

    private void OnDestroy()
    {

        for (int i = 0; i < bindings.Count; i++)
        {
            HoverBinding binding = bindings[i];
            if (binding.EnterEntry != null)
                binding.EnterEntry.callback.RemoveListener(binding.EnterCallback);
            if (binding.ExitEntry != null)
                binding.ExitEntry.callback.RemoveListener(binding.ExitCallback);
        }

        bindings.Clear();
    }
}
