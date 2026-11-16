
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Game.NPC;
using Game.LLM;

namespace Game.UI
{
    public sealed class ChatUI : MonoBehaviour
    {
        [Header("NPC")]
        [SerializeField] private NPCChatController npc;

        [Header("Chat UI")]
        [SerializeField] private TMP_Text npcNameText;
        [SerializeField] private TMP_InputField messageInput;
        [SerializeField] private Button sendButton;
        [SerializeField] private Button clearButton;
        [SerializeField] private TMP_Text statusText;

        [Header("Message List")]
        [SerializeField] private Transform content;
        [SerializeField] private ChatMessageView messagePrefab;
        [SerializeField] private ScrollRect scrollRect;

        private bool isSending;

        private void Awake()
        {
            sendButton.onClick.AddListener(SendMessage);
            clearButton.onClick.AddListener(ClearConversation);

            messageInput.onSubmit.AddListener(
                OnInputSubmit
            );
        }

        private void Start()
        {
            if (npc == null)
            {
                SetStatus("NPC is not configured.");
                SetInteractable(false);
                return;
            }

            npcNameText.text = npc.CharacterName;
            SetStatus("You can start chatting.");
        }

        private void OnDestroy()
        {
            if (sendButton != null)
                sendButton.onClick.RemoveListener(SendMessage);

            if (clearButton != null)
                clearButton.onClick.RemoveListener(ClearConversation);

            if (messageInput != null)
                messageInput.onSubmit.RemoveListener(OnInputSubmit);
        }

        private void OnInputSubmit(string value)
        {
            SendMessage();
        }

        public void SendMessage()
        {
            if (isSending || npc == null)
                return;

            string text = messageInput.text.Trim();

            if (string.IsNullOrEmpty(text))
                return;

            messageInput.text = "";

            AddMessage("You", text, true);

            isSending = true;
            SetInteractable(false);
            SetStatus($"{npc.CharacterName} is thinking…");

            StartCoroutine(SendRoutine(text));
        }

        private IEnumerator SendRoutine(string text)
        {
            LLMResult result = null;

            yield return npc.SendMessageAsync(
                text,
                response => result = response
            );

            if (result != null && result.Success)
            {
                AddMessage(
                    npc.CharacterName,
                    result.Content,
                    false
                );

                SetStatus("You can keep chatting.");
            }
            else
            {
                string error = result != null
                    ? result.Error
                    : "No valid response received.";

                AddMessage(
                    "System",
                    $"Request failed: {error}",
                    false
                );

                SetStatus("Request failed. Check the network and API config.");
            }

            isSending = false;
            SetInteractable(true);
            messageInput.ActivateInputField();
        }

        private void AddMessage(
            string speaker,
            string text,
            bool isPlayer)
        {
            ChatMessageView view = Instantiate(
                messagePrefab,
                content
            );

            view.Setup(speaker, text, isPlayer);

            Canvas.ForceUpdateCanvases();

            if (content is RectTransform contentRect)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(
                    contentRect
                );
            }

            if (scrollRect != null)
            {
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }

        private void ClearConversation()
        {
            if (isSending || npc == null)
                return;

            npc.ClearHistory();

            for (int i = content.childCount - 1; i >= 0; i--)
            {
                Destroy(content.GetChild(i).gameObject);
            }

            SetStatus("Conversation cleared. You can start over.");
            messageInput.ActivateInputField();
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }

        private void SetInteractable(bool value)
        {
            if (sendButton != null)
                sendButton.interactable = value;

            if (clearButton != null)
                clearButton.interactable = value;

            if (messageInput != null)
                messageInput.interactable = value;
        }
    }
}
