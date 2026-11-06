export const TRACK_RADIUS_X = 58;
export const TRACK_RADIUS_Z = 38;
export const TRACK_HALF_WIDTH = 5.8;
const CURVE_EXPONENT = 0.72;
const TAU = Math.PI * 2;

export type TrackPoint = { x: number; z: number };
export type TrackPose = TrackPoint & { yaw: number };

export function trackPointAtAngle(angle: number, extra = 0): TrackPoint {
  const sin = Math.sin(angle);
  const cos = Math.cos(angle);
  return {
    x: Math.sign(sin) * Math.pow(Math.abs(sin), CURVE_EXPONENT) * (TRACK_RADIUS_X + extra),
    z: Math.sign(cos) * Math.pow(Math.abs(cos), CURVE_EXPONENT) * (TRACK_RADIUS_Z + extra),
  };
}

export function trackTangentAtAngle(angle: number): TrackPoint {
  const before = trackPointAtAngle(angle - 0.002);
  const after = trackPointAtAngle(angle + 0.002);
  const x = after.x - before.x;
  const z = after.z - before.z;
  const length = Math.hypot(x, z) || 1;
  return { x: x / length, z: z / length };
}

export function trackSideAtAngle(angle: number): TrackPoint {
  const tangent = trackTangentAtAngle(angle);
  return { x: tangent.z, z: -tangent.x };
}

/** progress is measured in visual laps; it is not a distance or race telemetry value. */
export function trackPoseAtLapProgress(progress: number, lateralOffset = 0): TrackPose {
  const angle = ((progress % 1) + 1) % 1 * TAU;
  const point = trackPointAtAngle(angle);
  const side = trackSideAtAngle(angle);
  const tangent = trackTangentAtAngle(angle);
  return {
    x: point.x + side.x * lateralOffset,
    z: point.z + side.z * lateralOffset,
    yaw: Math.atan2(tangent.x, tangent.z),
  };
}

/**
 * Maps the existing ID-based display grid onto the nearby start straight so the
 * first confirmed replay frame does not teleport karts. This is visual layout only.
 */
export function visualStartProgressFromGridX(x: number): number {
  const ratio = Math.max(-0.999, Math.min(0.999, x / TRACK_RADIUS_X));
  const sin = Math.sign(ratio) * Math.pow(Math.abs(ratio), 1 / CURVE_EXPONENT);
  return Math.asin(sin) / TAU;
}

export function visualLaneOffsetFromGridZ(z: number): number {
  return TRACK_RADIUS_Z - z;
}
