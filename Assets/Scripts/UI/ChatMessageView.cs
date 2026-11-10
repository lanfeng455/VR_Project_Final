using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed class ChatMessageView : MonoBehaviour
    {
        [SerializeField] private TMP_Text speakerText;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private Image background;

        [Header("Bubble Colors")]
        [SerializeField] private Color playerColor =
            new Color(0.78f, 0.87f, 1f);

        [SerializeField] private Color npcColor =
            new Color(0.88f, 0.88f, 0.88f);

        public void Setup(
            string speaker,
            string message,
            bool isPlayer)
        {
            speakerText.text = speaker;
            messageText.text = message;

            if (background != null)
            {
                background.color = isPlayer
                    ? playerColor
                    : npcColor;
            }
            
            if (rowLayout != null)
            {
                rowLayout.childAlignment = isPlayer
                    ? TextAnchor.UpperRight
                    : TextAnchor.UpperLeft;
            }
        }
        
        [SerializeField]
        private HorizontalLayoutGroup rowLayout;
    }
    
}