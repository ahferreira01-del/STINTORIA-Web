import assert from "node:assert/strict";
import test from "node:test";
import { createKartFairingGeometry } from "../artifacts/stintoria-web/src/scene/kartFairingGeometry.ts";

const profile = [
  { z: -1.2, centerY: 0.54, halfWidth: 0.18, halfHeight: 0.16 },
  { z: -0.4, centerY: 0.58, halfWidth: 0.34, halfHeight: 0.2 },
  { z: 0.55, centerY: 0.6, halfWidth: 0.26, halfHeight: 0.17 },
  { z: 1.5, centerY: 0.58, halfWidth: 0.05, halfHeight: 0.07 },
];

function normalOfTriangle(positions, indices, offset) {
  const point = (index) => positions.slice(index * 3, index * 3 + 3);
  const [aIndex, bIndex, cIndex] = indices.slice(offset, offset + 3);
  const a = point(aIndex);
  const b = point(bIndex);
  const c = point(cIndex);
  const ab = b.map((value, axis) => value - a[axis]);
  const ac = c.map((value, axis) => value - a[axis]);
  return [
    ab[1] * ac[2] - ab[2] * ac[1],
    ab[2] * ac[0] - ab[0] * ac[2],
    ab[0] * ac[1] - ab[1] * ac[0],
  ];
}

test("carenagem gera anéis fechados, extremidades e índices dentro dos limites", () => {
  const geometry = createKartFairingGeometry(profile, 16);
  const vertexCount = geometry.positions.length / 3;
  const expectedVertices = profile.length * 16 + 2;
  const expectedIndices = (profile.length - 1) * 16 * 6 + 16 * 6;

  assert.equal(vertexCount, expectedVertices);
  assert.equal(geometry.indices.length, expectedIndices);
  assert.ok(geometry.positions.every(Number.isFinite));
  assert.ok(geometry.indices.every((index) => index >= 0 && index < vertexCount));
});

test("faces laterais e tampas têm normais externas não degeneradas", () => {
  const geometry = createKartFairingGeometry(profile, 16);
  const sideNormal = normalOfTriangle(geometry.positions, geometry.indices, 0);
  const capStart = (profile.length - 1) * 16 * 6;
  const startNormal = normalOfTriangle(geometry.positions, geometry.indices, capStart);
  const endNormal = normalOfTriangle(geometry.positions, geometry.indices, capStart + 3);

  assert.ok(sideNormal[0] > 0);
  assert.ok(startNormal[2] < 0);
  assert.ok(endNormal[2] > 0);
  for (let offset = 0; offset < geometry.indices.length; offset += 3) {
    const normal = normalOfTriangle(geometry.positions, geometry.indices, offset);
    assert.ok(Math.hypot(...normal) > 1e-8);
  }
});

test("seções inválidas e perfis invertidos são rejeitados", () => {
  assert.throws(() => createKartFairingGeometry([profile[0]]), /duas seções/);
  assert.throws(() => createKartFairingGeometry(profile, 6), /oito segmentos/);
  assert.throws(
    () => createKartFairingGeometry([profile[0], { ...profile[1], z: profile[0].z }]),
    /eixo longitudinal/,
  );
  assert.throws(
    () => createKartFairingGeometry([profile[0], { ...profile[1], halfWidth: 0 }]),
    /dimensões inválidas/,
  );
});
