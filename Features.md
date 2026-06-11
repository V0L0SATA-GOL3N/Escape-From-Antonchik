## Animation Formulas

Normalized time:

$$
rawT = \operatorname{clamp}\!\left(\frac{elapsed}{pickupAnimationTime}, 0, 1\right)
$$

Object position interpolation:

$$
objectPos(t) = \operatorname{lerp}\!\left(startPosition, holdPointPosition, \operatorname{smoothstep}\!\left(0, 1, \operatorname{inverseLerp}(handReachPortion, 1, rawT)\right)\right)
$$

Camera follow offset from wrist:

$$
cameraFollowOffset(t) = \left(wristWorldPos(t) - startWristWorldPos\right) \cdot pickupCameraArmFollowStrength
$$

Camera forward pulse:

$$
cameraForwardPulse(t) = \sin(\pi \cdot rawT) \cdot pickupCameraForwardMove
$$
