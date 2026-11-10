using System;
using System.Collections;
using UnityEngine;
using DG.Tweening;

public class PresentationNailoong : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private TweenType tweenType;
    
    private enum TweenType
    {
        Translate,
        TranslateEase,
        TranslateAndScale,
        TranslateAndScaleAndLoop
    }
    
    // -4.82 4.47

    private void OnEnable()
    {
        switch (tweenType)
        {
            case TweenType.Translate:
            {
                target.transform.position = new Vector3(-4.82f, target.transform.position.y, target.transform.position.z);
                target.DOMoveX(4.47f, 1);
                break;
            }
            case TweenType.TranslateEase:
            {
                target.transform.position = new Vector3(-4.82f, target.transform.position.y, target.transform.position.z);
                target.DOMoveX(4.47f, 1).SetEase(Ease.OutSine);
                break;
            }
            case TweenType.TranslateAndScale:
            {
                target.transform.position = new Vector3(-4.82f, target.transform.position.y, target.transform.position.z);
                target.DOMoveX(4.47f, 1).SetEase(Ease.OutSine).OnComplete(() => {target.DOScale(new Vector3(1.2f, 1.2f, 1.2f), 1).OnComplete(() => {target.GetComponent<SpriteRenderer>().color = Color.red;});});
                break;
            }
            case TweenType.TranslateAndScaleAndLoop:
            {
                target.transform.position = new Vector3(-4.82f, target.transform.position.y, target.transform.position.z);
                target.DOMoveX(4.47f, 1).SetEase(Ease.OutSine).SetLoops(-1, LoopType.Yoyo);
                target.DOScale(new Vector3(1.2f, 1.2f, 1.2f), 1).SetDelay(1).SetLoops(-1, LoopType.Yoyo);
                break;
            }
        }
    }
    
}
