export interface KartGridDriver {
  driverId: string;
  name: string;
  teamId: string;
  teamName: string;
  isProtagonist: boolean;
}

export interface KartGridSlot extends KartGridDriver {
  row: number;
  column: number;
  x: number;
  z: number;
  yaw: number;
  teamColor: string;
}

const teamPalette = [
  "#167fa0",
  "#c34d43",
  "#d4a94e",
  "#507c58",
  "#765d9a",
  "#cb7447",
  "#3e75a2",
  "#ad527c",
  "#7b8b45",
  "#69737b",
] as const;

function compareId(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0;
}

export function createSchematicKartGrid(
  drivers: readonly KartGridDriver[],
): KartGridSlot[] {
  if (drivers.length !== 20) {
    throw new Error("A formação esquemática exige os 20 pilotos retornados pelo C#.");
  }

  const ids = new Set<string>();
  const teams = new Map<string, { name: string; count: number }>();
  for (const driver of drivers) {
    if (!driver.driverId.trim() || !driver.name.trim() ||
        !driver.teamId.trim() || !driver.teamName.trim()) {
      throw new Error("O motor C# retornou um piloto ou equipe sem identificação.");
    }
    if (ids.has(driver.driverId)) {
      throw new Error("O motor C# retornou pilotos duplicados; a formação não foi criada.");
    }
    ids.add(driver.driverId);
    const team = teams.get(driver.teamId);
    if (team && team.name !== driver.teamName) {
      throw new Error("O motor C# retornou nomes diferentes para a mesma equipe.");
    }
    teams.set(driver.teamId, {
      name: driver.teamName,
      count: (team?.count ?? 0) + 1,
    });
  }

  if (drivers.filter((driver) => driver.isProtagonist).length !== 1) {
    throw new Error("A formação precisa identificar exatamente um protagonista C#.");
  }
  if (teams.size !== 10 || [...teams.values()].some((team) => team.count !== 2)) {
    throw new Error("A formação esperava 10 equipes C# com dois pilotos cada.");
  }

  const teamIds = [...teams.keys()].sort(compareId);
  const teamColors = new Map(teamIds.map((teamId, index) => [
    teamId,
    teamPalette[index % teamPalette.length],
  ]));

  return [...drivers]
    .sort((left, right) => compareId(left.driverId, right.driverId))
    .map((driver, index) => {
      const row = Math.floor(index / 2);
      const column = index % 2;
      return {
        ...driver,
        row,
        column,
        // Only a display arrangement. It does not encode grid position or race progress.
        x: (row - 4.5) * 3.25,
        z: 38 + (column === 0 ? -2.4 : 2.4),
        yaw: Math.PI / 2,
        teamColor: teamColors.get(driver.teamId)!,
      };
    });
}
