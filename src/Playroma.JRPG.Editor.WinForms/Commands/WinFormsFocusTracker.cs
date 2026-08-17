namespace Playroma.JRPG.Editor.WinForms.Commands;

public static class WinFormsFocusTracker
{
    public static Control? FindFocusedControl()
    {
        Form? form = Form.ActiveForm;
        if (form is null) return null;
        Control? current = form.ActiveControl;
        while (current is ContainerControl container && container.ActiveControl is Control child)
            current = child;
        return current ?? form;
    }
}
