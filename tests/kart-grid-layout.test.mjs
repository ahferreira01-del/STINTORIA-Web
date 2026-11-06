import assert from 'node:assert/strict';
import test from 'node:test';
import { createSchematicKartGrid } from '../artifacts/stintoria-web/src/scene/kartGridLayout.ts';

function roster() {
  return Array.from({ length: 20 }, (_, index) => ({
    driverId: `p${String(index + 1).padStart(2, '0')}`,
    name: `Piloto ${index + 1}`,
    teamId: `t${String(Math.floor(index / 2) + 1).padStart(2, '0')}`,
    teamName: `Equipe ${Math.floor(index / 2) + 1}`,
    isProtagonist: index === 6,
  }));
}

test('grid esquemático gera 20 posições únicas sem campo de classificação', () => {
  const slots = createSchematicKartGrid(roster());
  assert.equal(slots.length, 20);
  assert.equal(new Set(slots.map((slot) => slot.driverId)).size, 20);
  assert.equal(new Set(slots.map((slot) => `${slot.x}:${slot.z}`)).size, 20);
  assert.equal(slots.filter((slot) => slot.isProtagonist).length, 1);
  assert.ok(slots.every((slot) => !('position' in slot) && !('elapsedMs' in slot)));
});

test('a disposição e as pinturas são determinísticas e independentes da ordem de entrada', () => {
  const first = createSchematicKartGrid(roster());
  const shuffled = createSchematicKartGrid(roster().reverse());
  assert.deepEqual(first, shuffled);
  for (let team = 1; team <= 10; team += 1) {
    const members = first.filter((slot) => slot.teamId === `t${String(team).padStart(2, '0')}`);
    assert.equal(members.length, 2);
    assert.equal(members[0].teamColor, members[1].teamColor);
  }
  assert.equal(new Set(first.map((slot) => slot.teamColor)).size, 10);
});

test('recusa roster incompleto, pilotos duplicados e identificação protagonista ambígua', () => {
  const complete = roster();
  assert.throws(() => createSchematicKartGrid(complete.slice(0, 19)), /20 pilotos/);
  assert.throws(() => createSchematicKartGrid([
    ...complete.slice(0, 19),
    { ...complete[19], driverId: complete[0].driverId },
  ]), /duplicados/);
  assert.throws(() => createSchematicKartGrid(
    complete.map((driver) => ({ ...driver, isProtagonist: false })),
  ), /exatamente um protagonista/);
});

test('recusa composição de equipes que não corresponde ao campeonato Kart', () => {
  const invalid = roster().map((driver, index) => ({
    ...driver,
    teamId: index < 3 ? 't01' : driver.teamId,
    teamName: index < 3 ? 'Equipe 1' : driver.teamName,
  }));
  assert.throws(() => createSchematicKartGrid(invalid), /10 equipes C#/);
});
