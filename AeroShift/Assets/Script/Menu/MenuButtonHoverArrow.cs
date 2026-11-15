using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

// 하나의 매니저에서 메인 메뉴 Text의 마우스 호버를 관리합니다.
// Canvas(또는 빈 오브젝트)에 한 번만 붙이고 Menu Texts에 Text를 연결하세요.
[DisallowMultipleComponent]
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

            // Button이 있으면 버튼 전체, 없으면 Text 영역을 호버 대상으로 사용합니다.
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

            // 콜백에서 매니저가 비활성화되었거나 파괴되었으면 접근하지 않습니다.
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
        // Unity 오브젝트는 Destroy 후 C# 참조가 남을 수 있으므로 Unity null 검사.
        if (binding.Label == null || binding.IsHovered == hovered)
            return;

        binding.IsHovered = hovered;
        binding.Label.text = hovered
            ? HoverPrefix + binding.OriginalText
            : binding.OriginalText;
    }

    private void OnDisable()
    {
        // 씬 종료 중 Text가 먼저 파괴되었을 수 있으므로 살아있는 Text만 복구.
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
        // 파괴 중인 EventTrigger/UI 컴포넌트에 접근하지 않고
        // 관리 중인 C# 이벤트 콜백만 안전하게 해제합니다.
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
