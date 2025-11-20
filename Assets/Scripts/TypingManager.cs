using UnityEngine;
using Unity.Netcode;
using TMPro;
using UnityEngine.UI;
using System.Collections;
using System;

public class TypingManager : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI textBox;
    [SerializeField] private RaceManager raceManager;
    private bool editable = true;

    public void Awake()
    {
        Reset();
    }

    public void AddLetter(string letter)
    {
        if (!editable) return;
        textBox.color = Color.white;
        if (textBox.text.Length < 12)
        {
            textBox.text += letter.ToUpper();
        }
    }

    public void DeleteLetter()
    {
        if (!editable) return;
        textBox.color = Color.white;
        if (textBox.text.Length != 0)
        {
            textBox.text = textBox.text.Remove(textBox.text.Length - 1, 1);
        }
    }

    public void SubmitWord()
    {
        if (!editable) return;
        if (raceManager.SubmitWord(textBox.text))
        {
            textBox.color = Color.green;
            editable = false;
        }
        else
        {
            textBox.color = Color.red;
            editable = false;
        }
    }

    public void Reset()
    {
        textBox.text = "";
        textBox.color = Color.white;
        editable = true;
    }
}