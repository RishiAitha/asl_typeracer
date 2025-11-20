using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class KeyboardButtonController : MonoBehaviour
{
    [SerializeField] Image containerBorderImage;
    [SerializeField] Image containerFillImage;
    [SerializeField] Image containerIcon;
    [SerializeField] TextMeshProUGUI containerText;
    [SerializeField] TextMeshProUGUI containerActionText;
    
    private TypingManager typingManager;
    
    private void Start() 
    {
        SetContainerBorderColor(ColorDataStore.GetKeyboardBorderColor());
        SetContainerFillColor(ColorDataStore.GetKeyboardFillColor());
        SetContainerTextColor(ColorDataStore.GetKeyboardTextColor());
        SetContainerActionTextColor(ColorDataStore.GetKeyboardActionTextColor());
        typingManager = FindObjectsByType<TypingManager>(FindObjectsSortMode.None)[0];
    }
    
    public void SetContainerBorderColor(Color color) => containerBorderImage.color = color;
    public void SetContainerFillColor(Color color) => containerFillImage.color = color;
    public void SetContainerTextColor(Color color) => containerText.color = color;
    public void SetContainerActionTextColor(Color color) 
    { 
        containerActionText.color = color;
        containerIcon.color = color;
    }
    
    public void AddLetter() 
    {
        if (typingManager != null) 
        {
            typingManager.AddLetter(containerText.text);
        } 
    }
    
    public void DeleteLetter() 
    { 
        if (typingManager != null) 
        {
            typingManager.DeleteLetter();
        }
    }
    
    public void SubmitWord() 
    {
        if (typingManager != null) 
        {
            typingManager.SubmitWord();
        }
    }
}