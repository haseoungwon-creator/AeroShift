using UnityEngine;
using UnityEngine.UI;

public class MissileWarning : MonoBehaviour
{
    [SerializeField] Image warningImage;
    [SerializeField] float warningDuration = 2f;
    [SerializeField] float blinkInterval = 0.2f;

    private PoolManager poolManager;
    private float elapsedTime;
    private float blinkTime;
    private bool isVisible;

    public void Initialize(PoolManager pool)
    {
        poolManager = pool;
    }

    public void Show(Vector2 screenPosition, Vector3 direction)
    {
        RectTransform rectTransform = warningImage.rectTransform;
        rectTransform.position = screenPosition;

        float rotation = 0f;

        if (direction == Vector3.back)
            rotation = 180f;
        else if (direction == Vector3.forward)
            rotation = 0f;
        else if (direction == Vector3.left)
            rotation = 90f;
        else if (direction == Vector3.right)
            rotation = -90f;

        rectTransform.rotation = Quaternion.Euler(0f, 0f, rotation);

        elapsedTime = 0f;
        blinkTime = 0f;
        isVisible = true;
        warningImage.enabled = true;
    }

    private void Update()
    {
        if (!isVisible) return;

        elapsedTime += Time.deltaTime;
        blinkTime += Time.deltaTime;

        if (blinkTime >= blinkInterval)
        {
            blinkTime = 0f;
            warningImage.enabled = !warningImage.enabled;
        }

        if (elapsedTime >= warningDuration)
            Hide();
    }

    private void Hide()
    {
        isVisible = false;
        warningImage.enabled = false;
        if (poolManager != null)
            poolManager.Return(gameObject);
    }
}
