using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button), typeof(Image))]
public sealed class UIButtonHoverStyle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [SerializeField] Color normalBackground = Color.white;
    [SerializeField] Color hoverBackground = new(0.05f, 0.43f, 0.78f, 1f);
    [SerializeField] Color normalText = new(0.025f, 0.12f, 0.22f, 1f);
    [SerializeField] Color hoverText = Color.white;

    Image background;
    TMP_Text label;

    void Awake()
    {
        background = GetComponent<Image>();
        label = GetComponentInChildren<TMP_Text>(true);
        SetHovered(false);
    }

    public void OnPointerEnter(PointerEventData eventData) => SetHovered(true);
    public void OnPointerExit(PointerEventData eventData) => SetHovered(false);
    public void OnSelect(BaseEventData eventData) => SetHovered(true);
    public void OnDeselect(BaseEventData eventData) => SetHovered(false);

    void OnDisable() => SetHovered(false);

    void SetHovered(bool hovered)
    {
        if (background == null) background = GetComponent<Image>();
        if (label == null) label = GetComponentInChildren<TMP_Text>(true);
        background.color = hovered ? hoverBackground : normalBackground;
        if (label != null) label.color = hovered ? hoverText : normalText;
    }
}
