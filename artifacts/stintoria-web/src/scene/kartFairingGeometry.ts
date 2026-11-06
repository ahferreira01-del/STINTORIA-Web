export type KartFairingSection = Readonly<{
  z: number;
  centerY: number;
  halfWidth: number;
  halfHeight: number;
}>;

export type KartFairingGeometry = Readonly<{
  positions: number[];
  indices: number[];
}>;

export function createKartFairingGeometry(
  sections: readonly KartFairingSection[],
  radialSegments = 16,
): KartFairingGeometry {
  if (sections.length < 2) {
    throw new Error("A carenagem precisa de ao menos duas seções.");
  }
  if (!Number.isInteger(radialSegments) || radialSegments < 8) {
    throw new Error("A malha precisa de pelo menos oito segmentos radiais.");
  }

  for (let index = 0; index < sections.length; index += 1) {
    const section = sections[index];
    if (
      !Number.isFinite(section.z) ||
      !Number.isFinite(section.centerY) ||
      !Number.isFinite(section.halfWidth) ||
      !Number.isFinite(section.halfHeight) ||
      section.halfWidth <= 0 ||
      section.halfHeight <= 0
    ) {
      throw new Error("A seção da carenagem contém dimensões inválidas.");
    }
    if (index > 0 && section.z <= sections[index - 1].z) {
      throw new Error("As seções da carenagem precisam avançar pelo eixo longitudinal.");
    }
  }

  const positions: number[] = [];
  const indices: number[] = [];
  const ringSize = radialSegments;

  for (const section of sections) {
    for (let side = 0; side < radialSegments; side += 1) {
      const angle = (side / radialSegments) * Math.PI * 2;
      positions.push(
        section.halfWidth * Math.cos(angle),
        section.centerY + section.halfHeight * Math.sin(angle),
        section.z,
      );
    }
  }

  for (let section = 0; section < sections.length - 1; section += 1) {
    const currentRing = section * ringSize;
    const nextRing = (section + 1) * ringSize;
    for (let side = 0; side < radialSegments; side += 1) {
      const nextSide = (side + 1) % radialSegments;
      const current = currentRing + side;
      const currentNext = currentRing + nextSide;
      const next = nextRing + side;
      const nextNext = nextRing + nextSide;

      indices.push(current, currentNext, nextNext, current, nextNext, next);
    }
  }

  const startCenter = positions.length / 3;
  positions.push(0, sections[0].centerY, sections[0].z);
  const endCenter = positions.length / 3;
  const lastSection = sections.length - 1;
  positions.push(0, sections[lastSection].centerY, sections[lastSection].z);
  const lastRing = lastSection * ringSize;

  for (let side = 0; side < radialSegments; side += 1) {
    const nextSide = (side + 1) % radialSegments;
    indices.push(startCenter, nextSide, side);
    indices.push(endCenter, lastRing + side, lastRing + nextSide);
  }

  return { positions, indices };
}
