using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TenDays.StoryApartment
{
    public sealed class StoryApartmentExplore : MonoBehaviour
    {
        public enum Step { WatchTV, EnterBedroom, TurnOnLight, ReadCase, AskHost, PinCase, FreeExplore }
        public enum ActionKind { HostTV, Switch, Case, Board, Clue }

        [System.Serializable]
        public class Point
        {
            public Collider target;
            public string title;
            [TextArea] public string details;
            public ActionKind action;
        }

        public Camera eye;
        public CharacterController body;
        public TMP_Text status;
        public Font fontSource;
        public GameObject bedroomDoor, sideRoomDoors, caseNote, furnishedBedroom, pinnedSummary, tvGlow;
        public Light bedroomLight;
        public GameObject blackout;
        public GameObject bedroomWindowCover;
        public Point[] points;

        readonly HashSet<Point> clues = new();
        readonly RaycastHit[] hits = new RaycastHit[24];
        Step step;
        TMP_FontAsset font;
        float pitch, fall, readUntil;
        bool captured;
        string reading = "";
        public Step CurrentStep => step;

        void Start()
        {
            font = TMP_FontAsset.CreateFontAsset(fontSource);
            font.isMultiAtlasTexturesEnabled=true;
            status.font = font;
            ApplyStep();
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame) ReleaseMouse();
            if (!captured && mouse.leftButton.wasPressedThisFrame)
            {
                captured = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (captured)
            {
                var delta = mouse.delta.ReadValue() * .095f;
                pitch = Mathf.Clamp(pitch - delta.y,-78,78);
                transform.Rotate(0,delta.x,0);
                eye.transform.localRotation = Quaternion.Euler(pitch,0,0);
                var axis = Vector2.ClampMagnitude(new Vector2(
                    (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0)),1);
                fall = body.isGrounded ? -2 : Mathf.Max(fall - 14*Time.deltaTime,-15);
                body.Move((transform.right*axis.x*2.05f + transform.forward*axis.y*2.05f + Vector3.up*fall)*Time.deltaTime);
            }

            if(step==Step.EnterBedroom && transform.position.x>6.62f && transform.position.z>4.92f)
                Advance(Step.TurnOnLight,"主持人：来，先把灯打开。");
            var aimed = FindAimedPoint();
            if (captured && keyboard.eKey.wasPressedThisFrame && aimed != null)
                Inspect(aimed);
            if(captured && step==Step.AskHost && keyboard.qKey.wasPressedThisFrame)
                AskWriterQuestion();
            UpdatePrompt(aimed);
        }

        public void Inspect(Point point)
        {
            switch (point.action)
            {
                case ActionKind.HostTV when step==Step.WatchTV:
                    Advance(Step.EnterBedroom,"主持人：编辑来到作家住所，在卧室发现了他。门窗完好。请去卧室看看。");
                    break;
                case ActionKind.Switch when step==Step.TurnOnLight:
                    Advance(Step.ReadCase,"灯亮了。先查看床边的案件简述。");
                    break;
                case ActionKind.Case when step==Step.ReadCase:
                    Advance(Step.AskHost,point.details);
                    break;
                case ActionKind.Board when step==Step.PinCase:
                    Advance(Step.FreeExplore,"已把案件简述贴到白板。阳台、卫生间和厨房开放。");
                    break;
                case ActionKind.Clue when step>=Step.PinCase:
                    clues.Add(point);
                    Read(point.title+"\n"+point.details);
                    break;
                default:
                    Read(point.details);
                    break;
            }
        }

        public void AskWriterQuestion()
        {
            if (step==Step.AskHost)
                Advance(Step.PinCase,"你：B 是小说家吗？\n主持人：是。书桌和书架已出现，把案件简述带回客厅贴到白板。");
        }

        void Advance(Step next,string message)
        {
            step=next;
            ApplyStep();
            Read(message);
        }

        public void ApplyStep()
        {
            bedroomDoor.SetActive(step==Step.WatchTV || step==Step.TurnOnLight || step==Step.ReadCase || step==Step.AskHost);
            sideRoomDoors.SetActive(step<Step.FreeExplore);
            bedroomLight.enabled=step>=Step.ReadCase;
            blackout.SetActive(step==Step.TurnOnLight);
            if (bedroomWindowCover) bedroomWindowCover.SetActive(step < Step.ReadCase);
            caseNote.SetActive(step>=Step.ReadCase);
            furnishedBedroom.SetActive(step>=Step.PinCase);
            pinnedSummary.SetActive(step>=Step.FreeExplore);
            tvGlow.SetActive(step>=Step.EnterBedroom);
        }

        void Read(string message) { reading=message;readUntil=Time.unscaledTime+12; }

        Point FindAimedPoint()
        {
            int count=Physics.RaycastNonAlloc(eye.transform.position,eye.transform.forward,hits,2.6f,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide);
            Point point=null;
            float closest=float.MaxValue, solid=float.MaxValue;
            for(int i=0;i<count;i++)
            {
                var hit=hits[i];
                if(!hit.collider.isTrigger && hit.collider!=body) solid=Mathf.Min(solid,hit.distance);
                foreach(var candidate in points)
                    if(hit.collider==candidate.target && hit.distance<closest) { point=candidate;closest=hit.distance; }
            }
            return closest<=solid+.025f?point:null;
        }

        void UpdatePrompt(Point aimed)
        {
            string goal=step switch
            {
                Step.WatchTV=>"客厅 · 到电视前，按 E 听主持人介绍",
                Step.EnterBedroom=>"从客厅右侧门口进入卧室",
                Step.TurnOnLight=>"卧室 · 看向门边发光的开关，按 E",
                Step.ReadCase=>"看向床边的案件简述，按 E 查看",
                Step.AskHost=>"按 Q 提问：B 是小说家吗？",
                Step.PinCase=>"带着案件简述回客厅，看向白板，按 E 贴上",
                _=>$"自由探索 · 已调查 {clues.Count} / 4 件补充线索"
            };
            var hint=captured?"WASD 移动　鼠标转向　E 调查　Esc 释放鼠标":"点击 Game 画面开始";
            status.text=goal+"\n"+hint+(aimed==null?"":"\nE · "+aimed.title)
                +(Time.unscaledTime<readUntil?"\n"+reading:"");
        }

        void ReleaseMouse()
        {
            if(!captured)return;
            captured=false;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
        }
        void OnApplicationFocus(bool focus) { if(!focus)ReleaseMouse(); }
        void OnDestroy() { ReleaseMouse();if(font)Destroy(font); }
    }
}
