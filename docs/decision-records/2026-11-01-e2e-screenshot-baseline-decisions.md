# E2E screenshot baseline decisions

The visual-regression references cover the desktop Move-mode toolbar, the
desktop selected-person detail card, and import-warning toasts on desktop and
narrow mobile emulation. The detail-card reference uses the imported-family
fixture and captures only Ada North's dialog; it does not baseline the scene
behind it. The warning references also capture only the toast, not the imported
scene.

The sample graph is not screenshot-baselined: at the canonical viewports, the
scene extends beyond the visible area and some labels are clipped or obscured.
Behavioural tests still exercise the scene and camera with real browser input.
The narrow mobile Move-mode toolbar is functionally tested but has no screenshot
reference because the toolbar overflows the viewport and clips controls. These
visual states are not accepted as baselines until their existing behaviour is
addressed separately.

No reference image is stored for known-defect states. The imported-family
overview is excluded because its first node label is missing ([#116](https://github.com/bitsforthought/Wilnaatahl/issues/116));
multi-selection and moved-node connectors cannot be reached while Move-mode
projected selection is blocked ([#117](https://github.com/bitsforthought/Wilnaatahl/issues/117));
and a narrow-screen selected-detail image is blocked by View-mode touch
interception ([#118](https://github.com/bitsforthought/Wilnaatahl/issues/118)).
Edge-positioned detail placement is also not captured: the current synthetic
fixture has no node at an edge, and this layer does not alter graph layout to
manufacture that state. These remain documented gaps rather than approved
visual behaviour.
