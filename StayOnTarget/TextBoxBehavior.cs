using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StayOnTarget;

public class TextBoxBehavior {
    // 1. Register the Attached Property
    public static readonly DependencyProperty IsNumericOnlyProperty =
        DependencyProperty.RegisterAttached(
            "IsNumericOnly",
            typeof(bool),
            typeof(TextBoxBehavior),
            new PropertyMetadata(false, OnIsNumericOnlyChanged));

    // Getter for XAML
    public static bool GetIsNumericOnly(DependencyObject obj) => (bool)obj.GetValue(IsNumericOnlyProperty);
        
    // Setter for XAML
    public static void SetIsNumericOnly(DependencyObject obj, bool value) => obj.SetValue(IsNumericOnlyProperty, value);

    // 2. Watch for the property being set to 'True' in XAML
    private static void OnIsNumericOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox textBox)
        {
            bool isNumeric = (bool)e.NewValue;

            if (isNumeric)
            {
                textBox.PreviewTextInput += NumberValidationTextBox;
                textBox.PreviewKeyDown += TextBox_PreviewKeyDown_DisallowSpaceKey;
                DataObject.AddPastingHandler(textBox, TextBoxPasting);
            }
            else
            {
                textBox.PreviewTextInput -= NumberValidationTextBox;
                textBox.PreviewKeyDown -= TextBox_PreviewKeyDown_DisallowSpaceKey;
                DataObject.RemovePastingHandler(textBox, TextBoxPasting);
            }
        }
    }
    public static void TextBox_PreviewKeyDown_DisallowSpaceKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            e.Handled = true; // Block the space key
        }
    }
    // Intercepts direct keyboard input
    public static void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            // Explicitly block the minus sign if the cursor is not at the very beginning
            if (e.Text == "-" && textBox.SelectionStart > 0)
            {
                e.Handled = true;
                return;
            }

            string currentText = textBox.Text;
            int selectionStart = textBox.SelectionStart;
            int selectionLength = textBox.SelectionLength;

            string proposedText = currentText.Remove(selectionStart, selectionLength)
                .Insert(selectionStart, e.Text);

            e.Handled = !IsValidDecimal(proposedText);
        }
    }
    
    // Helper to validate if the final string is a valid partial decimal number
    private static bool IsValidDecimal(string text)
    {
        // ^-? ensures the minus sign can ONLY exist at the very start of the string
        string pattern = @"^-?[0-9]*\.?[0-9]*$";
        return Regex.IsMatch(text, pattern);
    }

    // Intercepts copy-pasted text
    public static void TextBoxPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            string text = (string)e.DataObject.GetData(typeof(string));
            string pattern = @"^-?[0-9]*\.?[0-9]*$";
            Regex regex = new Regex(pattern);
                
            if (regex.IsMatch(text))
            {
                e.CancelCommand(); // Cancels the paste action if it contains non-numbers
            }
        }
        else
        {
            e.CancelCommand(); // Cancels pasting of non-text data (like images)
        }
    }
}