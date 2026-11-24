// From Uiverse.io by DaniloMGutavo - https://uiverse.io/DaniloMGutavo/stupid-mouse-85
// MIT License, Copyright (c) DaniloMGutavo (Danilo_Morais)
using System.Windows;

namespace FrameNote;

internal sealed class CheckboxMotionStates : VisualStateManager
{
    protected override bool GoToStateCore(FrameworkElement control, FrameworkElement stateGroupsRoot,
        string stateName, VisualStateGroup group, VisualState state, bool useTransitions)
    {
        // CheckBox also requests focus and pointer states handled by template triggers.
        if (state is null) return false;
        bool animate = useTransitions && control.IsVisible && SystemParameters.ClientAreaAnimation
            && !SystemParameters.HighContrast;
        return base.GoToStateCore(control, stateGroupsRoot, stateName, group, state, animate);
    }
}
