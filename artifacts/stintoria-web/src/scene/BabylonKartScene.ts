import { ArcRotateCamera } from "@babylonjs/core/Cameras/arcRotateCamera";
import { Camera } from "@babylonjs/core/Cameras/camera";
import { UniversalCamera } from "@babylonjs/core/Cameras/universalCamera";
import { PBRMaterial } from "@babylonjs/core/Materials/PBR/pbrMaterial";
import { StandardMaterial } from "@babylonjs/core/Materials/standardMaterial";
import { DynamicTexture } from "@babylonjs/core/Materials/Textures/dynamicTexture";
import { Color3, Color4 } from "@babylonjs/core/Maths/math.color";
import { Vector3 } from "@babylonjs/core/Maths/math.vector";
import { Mesh } from "@babylonjs/core/Meshes/mesh";
import { MeshBuilder } from "@babylonjs/core/Meshes/meshBuilder";
import { TransformNode } from "@babylonjs/core/Meshes/transformNode";
import { VertexData } from "@babylonjs/core/Meshes/mesh.vertexData";
import { VertexBuffer } from "@babylonjs/core/Buffers/buffer";
import { DirectionalLight } from "@babylonjs/core/Lights/directionalLight";
import { HemisphericLight } from "@babylonjs/core/Lights/hemisphericLight";
import { ShadowGenerator } from "@babylonjs/core/Lights/Shadows/shadowGenerator";
import { Engine } from "@babylonjs/core/Engines/engine";
import { Scene } from "@babylonjs/core/scene";
import {
  createSchematicKartGrid,
  type KartGridDriver,
  type KartGridSlot,
} from "./kartGridLayout";
import {
  TRACK_HALF_WIDTH,
  TRACK_RADIUS_X,
  TRACK_RADIUS_Z,
  trackPointAtAngle,
  trackPoseAtLapProgress,
  trackSideAtAngle,
  trackTangentAtAngle,
  visualLaneOffsetFromGridZ,
  visualStartProgressFromGridX,
} from "./kartTrackPath";
import {
  EMPTY_REPLAY_STATE,
  RaceReplayController,
  type RaceReplayViewState,
  type ReplaySpeed,
} from "./raceReplay";
import {
  createKartFairingGeometry,
  type KartFairingSection,
} from "./kartFairingGeometry";
import type { RaceSectorTimingSnapshot } from "../components/stintoria/raceSectorTimings";

export type KartCameraMode = "tv" | "aerial" | "pilot";
export type KartGraphicsMode = "quality" | "performance";

type CameraPose = { position: Vector3; target: Vector3 };
type CameraTransition = {
  elapsedMs: number;
  durationMs: number;
  arcHeight: number;
  mode: KartCameraMode;
  fromPosition: Vector3;
  fromTarget: Vector3;
};

type DriverLabel = {
  driverId: string;
  plane: Mesh;
  material: StandardMaterial;
  texture: DynamicTexture;
};

type DriverNumberPlate = DriverLabel;

const ROAD_RX = TRACK_RADIUS_X;
const ROAD_RZ = TRACK_RADIUS_Z;
const ROAD_HALF_WIDTH = TRACK_HALF_WIDTH;
const TRACK_SEGMENTS = 112;

const color = (hex: string) => Color3.FromHexString(hex);

function roundedTrackPoint(angle: number, extra = 0): Vector3 {
  const point = trackPointAtAngle(angle, extra);
  return new Vector3(point.x, 0, point.z);
}

function trackTangent(angle: number): Vector3 {
  const tangent = trackTangentAtAngle(angle);
  return new Vector3(tangent.x, 0, tangent.z);
}

function trackSide(angle: number): Vector3 {
  const side = trackSideAtAngle(angle);
  return new Vector3(side.x, 0, side.z);
}

function surfaceMaterial(
  scene: Scene,
  name: string,
  hex: string,
  roughness = 0.78,
  metallic = 0.05,
): PBRMaterial {
  const material = new PBRMaterial(name, scene);
  material.albedoColor = color(hex);
  material.roughness = roughness;
  material.metallic = metallic;
  material.environmentIntensity = 0.38;
  return material;
}

function simpleMaterial(scene: Scene, name: string, hex: string): StandardMaterial {
  const material = new StandardMaterial(name, scene);
  material.diffuseColor = color(hex);
  material.specularColor = new Color3(0.24, 0.25, 0.27);
  material.specularPower = 48;
  return material;
}

export class BabylonKartScene {
  readonly engine: Engine;
  readonly scene: Scene;

  private readonly canvas: HTMLCanvasElement;
  private readonly kartRoot: TransformNode;
  private readonly tvCamera: ArcRotateCamera;
  private readonly aerialCamera: ArcRotateCamera;
  private readonly pilotCamera: UniversalCamera;
  private readonly transitionCamera: UniversalCamera;
  private readonly shadowGenerator: ShadowGenerator;
  private readonly resizeHandler = () => this.engine.resize();
  private mode: KartCameraMode = "aerial";
  private graphics: KartGraphicsMode = "quality";
  private disposed = false;
  private readonly kartMeshes: Mesh[] = [];
  private sourceKartMeshes: Mesh[] = [];
  private readonly paintableKartMeshes = new Set<Mesh>();
  private readonly teamAccentKartMeshes = new Set<Mesh>();
  private readonly kartShadowMeshes = new Set<Mesh>();
  private readonly kartRoots = new Map<string, TransformNode>();
  private readonly gridClones: Mesh[] = [];
  private readonly gridLabels: DriverLabel[] = [];
  private readonly driverNumberPlates: DriverNumberPlate[] = [];
  private readonly teamPaintMaterials = new Map<string, PBRMaterial>();
  private readonly teamAccentMaterials = new Map<string, PBRMaterial>();
  private readonly wheelsByDriver = new Map<string, Mesh[]>();
  private readonly gridStarts = new Map<string, { x: number; z: number; yaw: number }>();
  private readonly visualStartLaps = new Map<string, number>();
  private readonly visualLaneOffsets = new Map<string, number>();
  private readonly replay = new RaceReplayController();
  private gridSignature = "";
  private raceIdentity = "";
  private selectedDriverId = "";
  private protagonistId = "";
  private gridCenter = new Vector3(0, 0, ROAD_RZ);
  private cameraTransition: CameraTransition | null = null;

  constructor(canvas: HTMLCanvasElement, onError?: (message: string) => void) {
    this.canvas = canvas;
    let stage = "criação do mecanismo WebGL";
    try {
      this.engine = new Engine(canvas, true, {
        antialias: true,
        adaptToDeviceRatio: true,
        stencil: true,
        powerPreference: "high-performance",
      });
      stage = "criação da cena";
      this.scene = new Scene(this.engine);
      this.scene.clearColor = new Color4(0.58, 0.69, 0.77, 1);
      this.scene.ambientColor = new Color3(0.24, 0.26, 0.29);
      this.scene.collisionsEnabled = false;

      stage = "iluminação e circuito";
      this.addLighting();
      this.addCircuit();
      stage = "montagem do kart e câmeras";
      this.kartRoot = new TransformNode("kart provisório", this.scene);
      this.kartRoot.position = new Vector3(0, 0.05, ROAD_RZ);
      this.kartRoot.rotation.y = Math.PI / 2;
      this.buildKart();
      this.sourceKartMeshes = [...this.kartMeshes];
      this.tvCamera = this.makeTvCamera();
      this.aerialCamera = this.makeAerialCamera();
      this.pilotCamera = this.makePilotCamera();
      this.transitionCamera = this.makeTransitionCamera();
      stage = "configuração de sombras";
      this.shadowGenerator = this.makeShadows();
      this.configureShadows();
      stage = "seleção da câmera inicial";
      this.setCameraMode("aerial");

      stage = "inicialização do loop";
      this.engine.runRenderLoop(() => {
        if (this.disposed) return;
        this.updateRacePresentation(this.engine.getDeltaTime());
        this.scene.render();
      });
      window.addEventListener("resize", this.resizeHandler);
    } catch (error) {
      console.error(`[STINTORIA] Falha na ${stage}.`, error);
      onError?.(
        `${stage}: ${error instanceof Error ? error.message : "erro desconhecido."}`,
      );
      throw error;
    }
  }

  setCameraMode(mode: KartCameraMode): void {
    if (this.disposed) return;
    const destination = this.cameraForMode(mode);
    if (this.mode === mode && this.scene.activeCamera === destination) return;
    const sourcePose = this.scene.activeCamera
      ? this.cameraPose(this.scene.activeCamera)
      : null;
    this.mode = mode;
    this.canvas.dataset.stintoriaCameraMode = mode;
    this.prepareCameraForMode(mode);
    if (!sourcePose || this.scene.activeCamera === destination) {
      this.scene.activeCamera?.detachControl();
      this.cameraTransition = null;
      this.scene.activeCamera = destination;
      destination.attachControl(this.canvas, true);
      return;
    }
    this.beginCameraTransition(mode, sourcePose);
  }

  private cameraForMode(mode: KartCameraMode): Camera {
    return mode === "tv"
      ? this.tvCamera
      : mode === "pilot"
        ? this.pilotCamera
        : this.aerialCamera;
  }

  private cameraPose(camera: Camera): CameraPose {
    camera.getViewMatrix(true);
    const position = camera.globalPosition.clone();
    const target = camera instanceof ArcRotateCamera
      ? camera.getTarget().clone()
      : position.add(camera.getDirection(new Vector3(0, 0, 1)).scale(18));
    return { position, target };
  }

  private prepareCameraForMode(mode: KartCameraMode): void {
    if (mode === "tv") {
      const root = this.selectedRoot();
      this.tvCamera.setTarget(root.position);
      this.tvCamera.alpha = this.tvAngleForHeading(root.rotation.y);
      this.tvCamera.beta = 0.92;
      this.tvCamera.radius = 38;
    } else if (mode === "aerial") {
      this.aerialCamera.setTarget(Vector3.Zero());
      this.aerialCamera.alpha = -Math.PI / 3;
      this.aerialCamera.beta = 0.72;
      this.aerialCamera.radius = 125;
    } else {
      this.pilotCamera.parent = this.selectedRoot();
      this.pilotCamera.position.set(0, 2.7, -6.5);
      this.pilotCamera.rotation.set(0, 0, 0);
    }
  }

  private beginCameraTransition(mode: KartCameraMode, source: CameraPose): void {
    this.scene.activeCamera?.detachControl();
    this.transitionCamera.parent = null;
    this.transitionCamera.position.copyFrom(source.position);
    this.transitionCamera.rotation.set(0, 0, 0);
    this.transitionCamera.setTarget(source.target);
    this.scene.activeCamera = this.transitionCamera;
    const destination = this.cameraPose(this.cameraForMode(mode));
    const travelDistance = Vector3.Distance(source.position, destination.position);
    this.cameraTransition = {
      elapsedMs: 0,
      durationMs: 460,
      arcHeight: Math.min(14, Math.max(2.5, travelDistance * 0.08)),
      mode,
      fromPosition: source.position.clone(),
      fromTarget: source.target.clone(),
    };
  }

  private updateCameraTransition(deltaMs: number): void {
    const transition = this.cameraTransition;
    if (!transition) return;
    const camera = this.cameraForMode(transition.mode);
    const destination = this.cameraPose(camera);
    transition.elapsedMs = Math.min(
      transition.durationMs,
      transition.elapsedMs + Math.max(0, Math.min(deltaMs, 100)),
    );
    const linear = transition.elapsedMs / transition.durationMs;
    const eased = linear * linear * (3 - 2 * linear);
    const position = Vector3.Lerp(transition.fromPosition, destination.position, eased);
    position.y += Math.sin(Math.PI * eased) * transition.arcHeight;
    this.transitionCamera.position.copyFrom(position);
    this.transitionCamera.setTarget(
      Vector3.Lerp(transition.fromTarget, destination.target, eased),
    );
    if (linear >= 1) {
      this.transitionCamera.detachControl();
      this.scene.activeCamera = camera;
      camera.attachControl(this.canvas, true);
      this.cameraTransition = null;
    }
  }

  private tvAngleForHeading(heading: number): number {
    return -heading - Math.PI / 2 - 0.28;
  }

  setGrid(drivers: readonly KartGridDriver[]): void {
    const slots = createSchematicKartGrid(drivers);
    const signature = JSON.stringify(slots.map(({ driverId, name, teamId, teamName, isProtagonist }) => ({
      driverId,
      name,
      teamId,
      teamName,
      isProtagonist,
    })));
    if (signature === this.gridSignature) return;

    this.clearGrid();
    this.gridSignature = signature;
    this.gridCenter = new Vector3(0, 0, ROAD_RZ);
    const protagonist = slots.find((slot) => slot.isProtagonist)!;
    this.protagonistId = protagonist.driverId;

    for (const slot of slots) {
      const root = slot.isProtagonist
        ? this.kartRoot
        : new TransformNode(`kart ${slot.driverId}`, this.scene);
      root.name = `kart ${slot.driverId}`;
      root.position = new Vector3(slot.x, 0.05, slot.z);
      root.rotation.y = slot.yaw;
      this.kartRoots.set(slot.driverId, root);
      this.gridStarts.set(slot.driverId, { x: slot.x, z: slot.z, yaw: slot.yaw });
      this.visualStartLaps.set(slot.driverId, visualStartProgressFromGridX(slot.x));
      this.visualLaneOffsets.set(slot.driverId, visualLaneOffsetFromGridZ(slot.z));

      const wheels: Mesh[] = slot.isProtagonist
        ? this.sourceKartMeshes.filter(
            (mesh) => mesh.metadata?.stintoriaWheelComponent === true,
          )
        : [];
      if (slot.isProtagonist) {
        this.applyTeamPaint(this.sourceKartMeshes, slot);
      } else {
        for (const source of this.sourceKartMeshes) {
          const clone = source.clone(`${slot.driverId} ${source.name}`, root);
          if (!(clone instanceof Mesh)) continue;
          if (this.teamAccentKartMeshes.has(source)) {
            clone.material = this.teamAccentMaterial(slot.teamId, slot.teamColor);
          } else if (this.paintableKartMeshes.has(source)) {
            clone.material = this.teamPaintMaterial(slot.teamId, slot.teamColor);
          }
          const castsShadow = this.kartShadowMeshes.has(source);
          clone.receiveShadows = castsShadow;
          this.gridClones.push(clone);
          if (castsShadow) this.shadowGenerator.addShadowCaster(clone);
          if (source.metadata?.stintoriaWheelComponent === true) wheels.push(clone);
        }
      }
      this.wheelsByDriver.set(slot.driverId, wheels);
      this.addDriverLabel(slot);
      this.addDriverNumberPlate(slot);
    }

    this.gridCenter = slots.reduce(
      (center, slot) => center.add(new Vector3(slot.x, 0, slot.z)),
      Vector3.Zero(),
    ).scale(1 / slots.length);
    const kartPartCount = [...this.kartRoots.values()].reduce(
      (count, root) => count + root.getChildren().filter((child) => child instanceof Mesh).length,
      0,
    );
    if (this.kartRoots.size !== 20 || kartPartCount !== 20 * this.sourceKartMeshes.length) {
      this.clearGrid();
      this.gridSignature = "";
      throw new Error("A cena não conseguiu instanciar integralmente os 20 karts do C#.");
    }
    this.canvas.dataset.stintoriaGridCount = String(this.kartRoots.size);
    this.canvas.dataset.stintoriaGridPartCount = String(kartPartCount);
    this.canvas.dataset.stintoriaGridDriverIds = slots.map((slot) => slot.driverId).join(",");
    this.canvas.dataset.stintoriaGridDriverTeamIds = slots
      .map((slot) => `${slot.driverId}:${slot.teamId}`)
      .join(",");
    this.canvas.dataset.stintoriaKartSourceParts = String(this.sourceKartMeshes.length);
    this.canvas.dataset.stintoriaKartWheelCountPerDriver = String(
      this.sourceKartMeshes.filter((mesh) => mesh.name.startsWith("pneu ")).length,
    );
    this.canvas.dataset.stintoriaWheelAnimationMeshesPerDriver = String(
      this.sourceKartMeshes.filter(
        (mesh) => mesh.metadata?.stintoriaWheelComponent === true,
      ).length,
    );
    this.canvas.dataset.stintoriaNumberPlateCount = String(this.driverNumberPlates.length);
    this.canvas.dataset.stintoriaNumberPlateDriverIds = this.driverNumberPlates
      .map((plate) => plate.driverId)
      .join(",");
    this.canvas.dataset.stintoriaTeamMaterialCount = String(
      this.teamPaintMaterials.size + this.teamAccentMaterials.size,
    );
    this.canvas.dataset.stintoriaSceneMeshCount = String(this.scene.meshes.length);
    this.canvas.dataset.stintoriaSceneMaterialCount = String(this.scene.materials.length);
    this.canvas.dataset.stintoriaShadowCasterCount = String(
      this.shadowGenerator.getShadowMap()?.renderList?.length ?? 0,
    );
    this.aerialCamera.alpha = -Math.PI / 3;
    this.aerialCamera.beta = 0.72;
    this.aerialCamera.radius = 125;
    this.selectDriver(this.protagonistId);
    this.aerialCamera.setTarget(Vector3.Zero());
  }

  setRaceSnapshot(snapshot: RaceSectorTimingSnapshot): boolean {
    const previousIdentity = this.raceIdentity;
    if (!this.replay.setSnapshot(snapshot)) return false;
    const nextIdentity =
      `${snapshot.identity.seed}|${snapshot.identity.round}|${snapshot.identity.circuitId}`;
    if (previousIdentity && previousIdentity !== nextIdentity) this.restoreGridFormation();
    this.raceIdentity = nextIdentity;
    this.canvas.dataset.stintoriaReplayTimingDrivers =
      String(this.replay.getState().driversWithTiming);
    return true;
  }

  getReplayState(): RaceReplayViewState {
    return this.replay.getState();
  }

  playVisualReplay(): void {
    this.replay.play();
  }

  pauseVisualReplay(): void {
    this.replay.pause();
  }

  restartVisualReplay(): void {
    this.replay.restart();
  }

  setVisualReplaySpeed(speed: ReplaySpeed): boolean {
    return this.replay.setSpeed(speed);
  }

  selectDriver(driverId: string): boolean {
    if (!this.kartRoots.has(driverId)) return false;
    const sourcePose = this.mode === "pilot" && this.scene.activeCamera
      ? this.cameraPose(this.scene.activeCamera)
      : null;
    this.selectedDriverId = driverId;
    this.canvas.dataset.stintoriaSelectedDriverId = driverId;
    if (this.mode === "pilot") {
      this.prepareCameraForMode("pilot");
      if (sourcePose) this.beginCameraTransition("pilot", sourcePose);
    }
    return true;
  }

  setGraphicsMode(mode: KartGraphicsMode): void {
    this.graphics = mode;
    this.engine.setHardwareScalingLevel(mode === "performance" ? 1.35 : 1);
    this.scene.shadowsEnabled = mode === "quality";
  }

  reset(): void {
    if (this.disposed) return;
    const sourcePose = this.scene.activeCamera
      ? this.cameraPose(this.scene.activeCamera)
      : null;
    this.prepareCameraForMode(this.mode);
    if (sourcePose) this.beginCameraTransition(this.mode, sourcePose);
  }

  dispose(): void {
    if (this.disposed) return;
    this.disposed = true;
    window.removeEventListener("resize", this.resizeHandler);
    this.engine.stopRenderLoop();
    this.scene.dispose();
    this.engine.dispose();
  }

  private addLighting(): void {
    const ambient = new HemisphericLight(
      "luz ambiente natural",
      new Vector3(0, 1, 0),
      this.scene,
    );
    ambient.intensity = 0.68;
    ambient.diffuse = color("#e5e9e9");
    ambient.groundColor = color("#50564f");

    const sun = new DirectionalLight(
      "sol de fim de tarde",
      new Vector3(-0.54, -1, -0.36),
      this.scene,
    );
    sun.position = new Vector3(36, 72, 44);
    sun.intensity = 1.75;
    sun.diffuse = color("#fff0d5");
  }

  private addCircuit(): void {
    const grass = surfaceMaterial(this.scene, "grama natural", "#3f5545", 0.96);
    const ground = MeshBuilder.CreateGround(
      "terreno do circuito",
      { width: 250, height: 200, subdivisions: 2 },
      this.scene,
    );
    ground.position.y = -0.34;
    ground.material = grass;
    ground.receiveShadows = true;

    const asphalt = surfaceMaterial(this.scene, "asfalto PBR", "#30373c", 0.9, 0.02);
    const pathArray = [
      this.makeTrackEdge(-ROAD_HALF_WIDTH),
      this.makeTrackEdge(ROAD_HALF_WIDTH),
    ];
    const road = MeshBuilder.CreateRibbon(
      "pista fechada",
      { pathArray, closePath: true, sideOrientation: Mesh.DOUBLESIDE },
      this.scene,
    );
    road.position.y = 0.015;
    road.material = asphalt;
    road.useVertexColors = true;
    road.receiveShadows = true;
    this.applySurfaceVariation(road, 0.045);

    const edgePaint = surfaceMaterial(this.scene, "faixa de bordo marfim", "#c9c7bc", 0.76, 0.01);
    const edgeLines = [
      MeshBuilder.CreateRibbon(
        "faixa contínua de bordo interna",
        {
          pathArray: [
            this.makeTrackEdge(ROAD_HALF_WIDTH - 0.84),
            this.makeTrackEdge(ROAD_HALF_WIDTH - 0.64),
          ],
          closePath: true,
          sideOrientation: Mesh.DOUBLESIDE,
        },
        this.scene,
      ),
      MeshBuilder.CreateRibbon(
        "faixa contínua de bordo externa",
        {
          pathArray: [
            this.makeTrackEdge(-ROAD_HALF_WIDTH + 0.64),
            this.makeTrackEdge(-ROAD_HALF_WIDTH + 0.84),
          ],
          closePath: true,
          sideOrientation: Mesh.DOUBLESIDE,
        },
        this.scene,
      ),
    ];
    for (const edgeLine of edgeLines) {
      edgeLine.position.y = 0.038;
      edgeLine.material = edgePaint;
      edgeLine.isPickable = false;
    }

    const gravel = surfaceMaterial(this.scene, "cascalho PBR de escape", "#706c60", 0.97, 0.01);
    const outerRunoff = MeshBuilder.CreateRibbon(
      "escape interno",
      {
        pathArray: [
          this.makeTrackEdge(ROAD_HALF_WIDTH + 3.8),
          this.makeTrackEdge(ROAD_HALF_WIDTH + 0.1),
        ],
        closePath: true,
        sideOrientation: Mesh.DOUBLESIDE,
      },
      this.scene,
    );
    outerRunoff.position.y = -0.055;
    outerRunoff.material = gravel;
    outerRunoff.useVertexColors = true;
    outerRunoff.receiveShadows = true;
    this.applySurfaceVariation(outerRunoff, 0.06);

    const innerRunoff = MeshBuilder.CreateRibbon(
      "escape externo",
      {
        pathArray: [
          this.makeTrackEdge(-ROAD_HALF_WIDTH - 0.1),
          this.makeTrackEdge(-ROAD_HALF_WIDTH - 2.8),
        ],
        closePath: true,
        sideOrientation: Mesh.DOUBLESIDE,
      },
      this.scene,
    );
    innerRunoff.position.y = -0.06;
    innerRunoff.material = gravel;
    innerRunoff.useVertexColors = true;
    this.applySurfaceVariation(innerRunoff, 0.06);

    const transitionBands = [
      MeshBuilder.CreateRibbon(
        "transição de escape interna",
        {
          pathArray: [
            this.makeTrackEdge(ROAD_HALF_WIDTH + 3.8),
            this.makeTrackEdge(ROAD_HALF_WIDTH + 5.1),
          ],
          closePath: true,
          sideOrientation: Mesh.DOUBLESIDE,
        },
        this.scene,
      ),
      MeshBuilder.CreateRibbon(
        "transição de escape externa",
        {
          pathArray: [
            this.makeTrackEdge(-ROAD_HALF_WIDTH - 2.8),
            this.makeTrackEdge(-ROAD_HALF_WIDTH - 4.1),
          ],
          closePath: true,
          sideOrientation: Mesh.DOUBLESIDE,
        },
        this.scene,
      ),
    ];
    for (const band of transitionBands) {
      band.position.y = -0.075;
      band.material = gravel;
      band.isPickable = false;
    }
    const transition = this.mergeStaticMeshes("faixas de transição de escape", transitionBands, gravel, true);
    transition.useVertexColors = true;
    this.applySurfaceVariation(transition, 0.04);

    const whiteCurb = surfaceMaterial(this.scene, "zebra marfim PBR", "#d2cec1", 0.78, 0.01);
    const redCurb = surfaceMaterial(this.scene, "zebra vermelha PBR", "#ad4942", 0.74, 0.02);
    const barrierMaterial = surfaceMaterial(this.scene, "barreira grafite", "#454d53", 0.72, 0.14);
    const redCurbParts: Mesh[] = [];
    const whiteCurbParts: Mesh[] = [];
    const barrierParts: Mesh[] = [];
    for (let i = 0; i < TRACK_SEGMENTS; i++) {
      const angle = (i / TRACK_SEGMENTS) * Math.PI * 2;
      const center = roundedTrackPoint(angle);
      const side = trackSide(angle);
      const yaw = Math.atan2(trackTangent(angle).x, trackTangent(angle).z);

      for (const sideSign of [-1, 1]) {
        const curb = MeshBuilder.CreateBox(
          `zebra ${i}-${sideSign}`,
          { width: 0.72, height: 0.13, depth: 2.55 },
          this.scene,
        );
        curb.position = center.add(side.scale(sideSign * (ROAD_HALF_WIDTH - 0.22)));
        curb.position.y = 0.12;
        curb.rotation.y = yaw;
        curb.material = i % 2 === 0 ? redCurb : whiteCurb;
        curb.isPickable = false;
        (i % 2 === 0 ? redCurbParts : whiteCurbParts).push(curb);
      }

      if (i % 3 === 0) {
        const barrier = MeshBuilder.CreateBox(
          `barreira ${i}`,
          { width: 0.44, height: 0.82, depth: 2.7 },
          this.scene,
        );
        barrier.position = center.add(side.scale(ROAD_HALF_WIDTH + 7.1));
        barrier.position.y = 0.42;
        barrier.rotation.y = yaw;
        barrier.material = barrierMaterial;
        barrier.receiveShadows = true;
        barrier.isPickable = false;
        barrierParts.push(barrier);
      }
    }
    this.mergeStaticMeshes("zebras vermelhas consolidadas", redCurbParts, redCurb);
    this.mergeStaticMeshes("zebras marfim consolidadas", whiteCurbParts, whiteCurb);
    this.mergeStaticMeshes("barreiras consolidadas", barrierParts, barrierMaterial, true);

    const line = simpleMaterial(this.scene, "linha de largada dourada", "#d3b06a");
    const stripe = MeshBuilder.CreateBox(
      "linha de largada",
      { width: 10.6, height: 0.045, depth: 0.68 },
      this.scene,
    );
    stripe.position = new Vector3(0, 0.075, ROAD_RZ + 0.2);
    stripe.material = line;

    this.addSectorMarkers();
    this.addPitBuilding();
    this.addPitLane();
    this.addRaceControlTower();
    this.addGrandstand();
    this.addTrees();
  }

  private addSectorMarkers(): void {
    const markerMaterial = simpleMaterial(this.scene, "referência visual de setor", "#d3b06a");
    for (let sector = 1; sector <= 3; sector += 1) {
      const progress = sector / 3;
      const angle = progress * Math.PI * 2;
      const point = trackPointAtAngle(angle);
      const tangent = trackTangentAtAngle(angle);
      const marker = MeshBuilder.CreateBox(
        `referência visual setor ${sector}`,
        { width: ROAD_HALF_WIDTH * 2, height: 0.055, depth: 0.34 },
        this.scene,
      );
      marker.position = new Vector3(point.x, 0.095, point.z);
      marker.rotation.y = Math.atan2(tangent.x, tangent.z);
      marker.material = markerMaterial;

      const label = MeshBuilder.CreatePlane(
        `rótulo visual setor ${sector}`,
        { width: 4.2, height: 1.05 },
        this.scene,
      );
      label.position = new Vector3(point.x, 4.1, point.z);
      label.billboardMode = Mesh.BILLBOARDMODE_ALL;
      const texture = new DynamicTexture(
        `textura visual setor ${sector}`,
        { width: 512, height: 128 },
        this.scene,
        true,
      );
      texture.hasAlpha = true;
      texture.drawText(
        `SETOR ${sector}`,
        22,
        82,
        "bold 42px Arial",
        "#e8cf8d",
        "rgba(13, 20, 25, 0.8)",
        true,
      );
      const material = new StandardMaterial(`material visual setor ${sector}`, this.scene);
      material.diffuseTexture = texture;
      material.opacityTexture = texture;
      material.useAlphaFromDiffuseTexture = true;
      material.disableLighting = true;
      material.backFaceCulling = false;
      label.material = material;
    }
  }

  private makeTrackEdge(offset: number): Vector3[] {
    const points: Vector3[] = [];
    for (let i = 0; i < TRACK_SEGMENTS; i++) {
      const angle = (i / TRACK_SEGMENTS) * Math.PI * 2;
      points.push(roundedTrackPoint(angle).add(trackSide(angle).scale(offset)));
    }
    return points;
  }

  private mergeStaticMeshes(
    name: string,
    parts: Mesh[],
    material: PBRMaterial | StandardMaterial,
    receiveShadows = false,
  ): Mesh {
    const merged = Mesh.MergeMeshes(parts, true, true, undefined, false, false);
    if (!merged) throw new Error(`Não foi possível consolidar a malha estática: ${name}.`);
    merged.name = name;
    merged.material = material;
    merged.isPickable = false;
    merged.receiveShadows = receiveShadows;
    return merged;
  }

  private applySurfaceVariation(mesh: Mesh, strength: number): void {
    const positions = mesh.getVerticesData(VertexBuffer.PositionKind);
    if (!positions) throw new Error(`A superfície ${mesh.name} não tem posições para variação.`);
    const colors: number[] = [];
    for (let index = 0; index < positions.length; index += 3) {
      const x = positions[index];
      const z = positions[index + 2];
      const wave =
        Math.sin(x * 0.19 + z * 0.11) * 0.58 +
        Math.cos(z * 0.23 - x * 0.07) * 0.42;
      const tone = 1 + strength * wave;
      colors.push(tone, tone, tone, 1);
    }
    mesh.setVerticesData(VertexBuffer.ColorKind, colors, false, 4);
  }

  private addPitBuilding(): void {
    const wall = surfaceMaterial(this.scene, "alvenaria dos boxes", "#aeb4b3", 0.84);
    const darkGlass = surfaceMaterial(this.scene, "portas envidraçadas dos boxes", "#263746", 0.28, 0.12);
    const gold = surfaceMaterial(this.scene, "friso dourado dos boxes", "#c2a15e", 0.38, 0.42);
    const roofMaterial = surfaceMaterial(this.scene, "cobertura grafite dos boxes", "#202a31", 0.72, 0.18);
    const building = MeshBuilder.CreateBox(
      "edifício de boxes de dez equipes",
      { width: 10.4, height: 5.8, depth: 64 },
      this.scene,
    );
    building.position = new Vector3(-80, 2.8, 0);
    building.material = wall;
    building.receiveShadows = true;

    const doors: Mesh[] = [];
    const piers: Mesh[] = [];
    const bayHeaders: Mesh[] = [];
    for (let bay = 0; bay < 10; bay++) {
      const z = -28.5 + bay * 6.3;
      const door = MeshBuilder.CreateBox(
        `porta de box ${String(bay + 1).padStart(2, "0")}`,
        { width: 0.12, height: 2.75, depth: 4.45 },
        this.scene,
      );
      door.position = new Vector3(-74.72, 1.62, z);
      door.material = darkGlass;
      door.isPickable = false;
      doors.push(door);

      const header = MeshBuilder.CreateBox(
        `identificação visual do box ${bay + 1}`,
        { width: 0.14, height: 0.42, depth: 4.5 },
        this.scene,
      );
      header.position = new Vector3(-74.68, 3.36, z);
      header.material = gold;
      header.isPickable = false;
      bayHeaders.push(header);

      if (bay < 9) {
        const pier = MeshBuilder.CreateBox(
          `pilar consolidável de box ${bay + 1}`,
          { width: 0.34, height: 3.7, depth: 0.34 },
          this.scene,
        );
        pier.position = new Vector3(-74.62, 1.95, z + 3.15);
        pier.material = roofMaterial;
        pier.isPickable = false;
        piers.push(pier);
      }
    }

    const roof = MeshBuilder.CreateBox(
      "cobertura consolidada dos boxes",
      { width: 12.2, height: 0.42, depth: 65.4 },
      this.scene,
    );
    roof.position = new Vector3(-80, 5.9, 0);
    roof.material = roofMaterial;
    roof.receiveShadows = true;

    const fascia = MeshBuilder.CreateBox(
      "friso frontal dos boxes",
      { width: 0.28, height: 0.5, depth: 65 },
      this.scene,
    );
    fascia.position = new Vector3(-74.72, 4.9, 0);
    fascia.material = gold;

    const signage = MeshBuilder.CreatePlane(
      "placa Motorsport Prestige dos boxes",
      { width: 8.4, height: 1.05 },
      this.scene,
    );
    signage.position = new Vector3(-74.48, 4.9, 0);
    signage.rotation.y = Math.PI / 2;
    const signTexture = new DynamicTexture(
      "textura da placa dos boxes",
      { width: 1024, height: 128 },
      this.scene,
      true,
    );
    signTexture.hasAlpha = true;
    signTexture.drawText(
      "STINTORIA  •  PADDOCK KART",
      28,
      86,
      "bold 54px Arial",
      "#e8cf8d",
      "#17232b",
      true,
    );
    const signMaterial = new StandardMaterial("material da placa dos boxes", this.scene);
    signMaterial.diffuseTexture = signTexture;
    signMaterial.emissiveColor = new Color3(0.35, 0.31, 0.2);
    signMaterial.disableLighting = true;
    signage.material = signMaterial;
    signage.isPickable = false;

    this.mergeStaticMeshes("portas consolidadas dos boxes", doors, darkGlass);
    this.mergeStaticMeshes("pilares consolidados dos boxes", piers, roofMaterial);
    this.mergeStaticMeshes("faixas de identificação dos boxes", bayHeaders, gold);
  }

  private addPitLane(): void {
    const asphalt = surfaceMaterial(this.scene, "asfalto visual do pit lane", "#30383a", 0.91, 0.015);
    const paint = surfaceMaterial(this.scene, "marcações visuais do pit lane", "#eee9db", 0.64, 0.02);
    const gold = surfaceMaterial(this.scene, "marcação dourada do pit lane", "#c2a15e", 0.5, 0.22);

    const lane = MeshBuilder.CreateBox(
      "faixa visual do pit lane sem acesso esportivo",
      { width: 8.4, height: 0.09, depth: 61 },
      this.scene,
    );
    lane.position = new Vector3(-68.9, 0.035, 0);
    lane.material = asphalt;
    lane.isPickable = false;
    lane.receiveShadows = true;

    const borderParts: Mesh[] = [];
    for (const x of [-73.05, -64.75]) {
      const line = MeshBuilder.CreateBox(
        "segmento de linha lateral do pit lane",
        { width: 0.16, height: 0.035, depth: 59.5 },
        this.scene,
      );
      line.position = new Vector3(x, 0.105, 0);
      line.material = paint;
      line.isPickable = false;
      borderParts.push(line);
    }

    const centerDashes: Mesh[] = [];
    for (let dash = 0; dash < 15; dash += 1) {
      const marker = MeshBuilder.CreateBox(
        `tracejado central visual ${dash + 1}`,
        { width: 0.1, height: 0.025, depth: 2.1 },
        this.scene,
      );
      marker.position = new Vector3(-68.9, 0.11, -27 + dash * 3.85);
      marker.material = gold;
      marker.isPickable = false;
      centerDashes.push(marker);
    }

    const stallOutlines: Mesh[] = [];
    for (let bay = 0; bay < 10; bay += 1) {
      const z = -28.5 + bay * 6.3;
      const sideLine = MeshBuilder.CreateBox(
        `limite visual box ${bay + 1}`,
        { width: 0.1, height: 0.025, depth: 4.45 },
        this.scene,
      );
      sideLine.position = new Vector3(-74.15, 0.115, z);
      sideLine.material = paint;
      sideLine.isPickable = false;
      stallOutlines.push(sideLine);
    }

    const laneFence = MeshBuilder.CreateBox(
      "barreira consolidada junto ao pit lane",
      { width: 0.32, height: 0.68, depth: 60 },
      this.scene,
    );
    laneFence.position = new Vector3(-63.95, 0.36, 0);
    laneFence.material = surfaceMaterial(this.scene, "material da barreira do pit lane", "#667074", 0.78, 0.12);
    laneFence.isPickable = false;

    this.mergeStaticMeshes("linhas laterais consolidadas do pit lane", borderParts, paint);
    this.mergeStaticMeshes("tracejado consolidado do pit lane", centerDashes, gold);
    this.mergeStaticMeshes("limites de boxes consolidados", stallOutlines, paint);
  }

  private addRaceControlTower(): void {
    const concrete = surfaceMaterial(this.scene, "torre de direção em grafite", "#303a40", 0.78, 0.12);
    const glass = surfaceMaterial(this.scene, "vidro da torre de direção", "#203748", 0.24, 0.18);
    const gold = surfaceMaterial(this.scene, "detalhes dourados da torre", "#c2a15e", 0.42, 0.35);
    const tower = MeshBuilder.CreateBox(
      "torre visual de direção de prova",
      { width: 8, height: 10.5, depth: 8 },
      this.scene,
    );
    tower.position = new Vector3(-80, 5.3, 40);
    tower.material = concrete;
    tower.receiveShadows = true;

    const windowBand = MeshBuilder.CreateBox(
      "faixa envidraçada da torre",
      { width: 8.15, height: 2.6, depth: 8.15 },
      this.scene,
    );
    windowBand.position = new Vector3(-80, 8.1, 40);
    windowBand.material = glass;

    const roof = MeshBuilder.CreateBox(
      "cobertura da torre de direção",
      { width: 9.1, height: 0.38, depth: 9.1 },
      this.scene,
    );
    roof.position = new Vector3(-80, 10.75, 40);
    roof.material = gold;

    const fascia = MeshBuilder.CreateBox(
      "placa visual da direção de prova",
      { width: 8.3, height: 0.5, depth: 0.22 },
      this.scene,
    );
    fascia.position = new Vector3(-75.85, 6.4, 40);
    fascia.material = gold;
  }

  private addGrandstand(): void {
    const concrete = surfaceMaterial(this.scene, "arquibancada de concreto", "#70787b", 0.88);
    const canopy = surfaceMaterial(this.scene, "cobertura azul-marinho", "#172b3b", 0.56, 0.12);
    const standRoot = new TransformNode("arquibancada", this.scene);
    standRoot.position = new Vector3(71, 0, -16);

    for (let row = 0; row < 5; row++) {
      const step = MeshBuilder.CreateBox(
        `degrau arquibancada ${row}`,
        { width: 18, height: 0.58, depth: 1.28 },
        this.scene,
      );
      step.parent = standRoot;
      step.position = new Vector3(0, 0.3 + row * 0.56, -row * 0.9);
      step.material = concrete;
      step.receiveShadows = true;
    }

    const roof = MeshBuilder.CreateBox(
      "cobertura da arquibancada",
      { width: 19, height: 0.38, depth: 6.8 },
      this.scene,
    );
    roof.parent = standRoot;
    roof.position = new Vector3(0, 3.55, -1.2);
    roof.material = canopy;

    for (let support = -1; support <= 1; support += 2) {
      const pillar = MeshBuilder.CreateBox(
        `pilar da arquibancada ${support}`,
        { width: 0.32, height: 3.5, depth: 0.34 },
        this.scene,
      );
      pillar.parent = standRoot;
      pillar.position = new Vector3(support * 8.3, 1.75, 1.3);
      pillar.material = concrete;
    }
  }

  private addTrees(): void {
    const bark = simpleMaterial(this.scene, "tronco", "#574536");
    const leaf = surfaceMaterial(this.scene, "folhagem", "#3e634a", 0.96);
    for (let i = 0; i < 24; i++) {
      const angle = (i / 24) * Math.PI * 2 + 0.08;
      const center = roundedTrackPoint(angle, ROAD_HALF_WIDTH + 18 + (i % 3) * 1.6);
      const trunk = MeshBuilder.CreateCylinder(
        `vegetação tronco ${i}`,
        { height: 2.4, diameterTop: 0.28, diameterBottom: 0.42, tessellation: 10 },
        this.scene,
      );
      trunk.position = center.add(new Vector3(0, 1.2, 0));
      trunk.material = bark;

      const crown = MeshBuilder.CreateSphere(
        `vegetação copa ${i}`,
        { diameter: 2.7 + (i % 2) * 0.35, segments: 12 },
        this.scene,
      );
      crown.position = center.add(new Vector3(0, 3.15, 0));
      crown.scaling.y = 0.82;
      crown.material = leaf;
    }
  }

  private buildKart(): void {
    const bodyPaint = surfaceMaterial(this.scene, "livery base compartilhada", "#167fa0", 0.34, 0.38);
    const carbon = surfaceMaterial(this.scene, "compósito grafite", "#20272c", 0.72, 0.16);
    const rubber = surfaceMaterial(this.scene, "borracha de competição", "#17191b", 0.96, 0.02);
    const alloy = surfaceMaterial(this.scene, "alumínio escovado", "#aeb7bb", 0.42, 0.7);
    const frameMetal = surfaceMaterial(this.scene, "tubos do chassi", "#77838a", 0.48, 0.64);
    const seatMaterial = surfaceMaterial(this.scene, "assento moldado", "#222629", 0.88, 0.04);
    const gold = surfaceMaterial(this.scene, "detalhe metálico", "#d5b16b", 0.4, 0.54);
    const visorMaterial = surfaceMaterial(this.scene, "viseira fumê", "#14212a", 0.22, 0.28);

    const addPart = (
      mesh: Mesh,
      material: PBRMaterial | StandardMaterial,
      paint: "base" | "accent" | "none" = "none",
      castsShadow = false,
    ) => {
      mesh.parent = this.kartRoot;
      mesh.material = material;
      mesh.isPickable = false;
      mesh.receiveShadows = castsShadow;
      this.kartMeshes.push(mesh);
      if (castsShadow) this.kartShadowMeshes.add(mesh);
      if (paint === "base") this.paintableKartMeshes.add(mesh);
      if (paint === "accent") this.teamAccentKartMeshes.add(mesh);
      return mesh;
    };
    const makeTube = (
      name: string,
      path: Vector3[],
      radius: number,
      material: PBRMaterial | StandardMaterial,
    ) => {
      const mesh = MeshBuilder.CreateTube(
        name,
        { path, radius, tessellation: 10, cap: Mesh.CAP_ALL },
        this.scene,
      );
      mesh.material = material;
      mesh.isPickable = false;
      return mesh;
    };
    const addTube = (
      name: string,
      path: Vector3[],
      radius: number,
      material: PBRMaterial | StandardMaterial,
      paint: "base" | "accent" | "none" = "none",
      castsShadow = false,
    ) => addPart(makeTube(name, path, radius, material), material, paint, castsShadow);
    const mergeParts = (
      name: string,
      parts: Mesh[],
      material: PBRMaterial | StandardMaterial,
      paint: "base" | "accent" | "none" = "none",
      castsShadow = false,
    ) => {
      const merged = Mesh.MergeMeshes(parts, true, true, undefined, false, false);
      if (!merged) throw new Error(`Não foi possível consolidar as peças: ${name}.`);
      merged.name = name;
      merged.material = material;
      return addPart(merged, material, paint, castsShadow);
    };
    const makeFairing = (
      name: string,
      centerX: number,
      sections: readonly KartFairingSection[],
      material: PBRMaterial,
    ) => {
      const mesh = new Mesh(name, this.scene);
      const geometry = createKartFairingGeometry(sections, 16);
      const normals: number[] = [];
      VertexData.ComputeNormals(geometry.positions, geometry.indices, normals);
      const data = new VertexData();
      data.positions = geometry.positions;
      data.indices = geometry.indices;
      data.normals = normals;
      data.applyToMesh(mesh, false);
      mesh.position.x = centerX;
      mesh.material = material;
      mesh.isPickable = false;
      return mesh;
    };

    // Tubular steel chassis: long rails, cross-bracing and raised front/rear hoops.
    const chassisParts: Mesh[] = [];
    for (const side of [-1, 1]) {
      chassisParts.push(makeTube(
        `longarina tubular ${side}`,
        [
          new Vector3(side * 0.48, 0.3, -1.28),
          new Vector3(side * 0.55, 0.3, -0.82),
          new Vector3(side * 0.54, 0.3, 0.5),
          new Vector3(side * 0.46, 0.33, 1.16),
        ],
        0.045,
        frameMetal,
      ));
      chassisParts.push(makeTube(
        `travessa da dianteira ${side}`,
        [
          new Vector3(side * 0.5, 0.32, 0.82),
          new Vector3(side * 0.6, 0.4, 1.2),
          new Vector3(side * 0.56, 0.42, 1.42),
        ],
        0.042,
        frameMetal,
      ));
    }
    chassisParts.push(makeTube(
      "arco tubular do para-choque dianteiro",
      [
        new Vector3(-0.62, 0.34, 1.34),
        new Vector3(-0.7, 0.38, 1.48),
        new Vector3(0, 0.4, 1.53),
        new Vector3(0.7, 0.38, 1.48),
        new Vector3(0.62, 0.34, 1.34),
      ],
      0.055,
      frameMetal,
    ));
    chassisParts.push(makeTube(
      "travessa tubular do eixo traseiro",
      [
        new Vector3(-0.67, 0.32, -1.3),
        new Vector3(-0.72, 0.4, -1.45),
        new Vector3(0, 0.42, -1.48),
        new Vector3(0.72, 0.4, -1.45),
        new Vector3(0.67, 0.32, -1.3),
      ],
      0.052,
      frameMetal,
    ));
    chassisParts.push(makeTube(
      "travessa central do chassi",
      [new Vector3(-0.55, 0.32, -0.18), new Vector3(0, 0.33, -0.18), new Vector3(0.55, 0.32, -0.18)],
      0.04,
      frameMetal,
    ));
    mergeParts("chassi tubular consolidado", chassisParts, frameMetal, "none");

    // Sculpted, low-poly PBR bodywork. The colored base remains team-specific.
    const bodyworkParts = [
      makeFairing(
      "carenagem aerodinâmica frontal",
      0,
      [
        { z: -0.5, centerY: 0.57, halfWidth: 0.3, halfHeight: 0.17 },
        { z: -0.12, centerY: 0.6, halfWidth: 0.34, halfHeight: 0.19 },
        { z: 0.48, centerY: 0.62, halfWidth: 0.27, halfHeight: 0.17 },
        { z: 1.06, centerY: 0.61, halfWidth: 0.17, halfHeight: 0.125 },
        { z: 1.5, centerY: 0.59, halfWidth: 0.045, halfHeight: 0.065 },
      ],
      bodyPaint,
      ),
    ];
    for (const side of [-1, 1]) {
      bodyworkParts.push(makeFairing(
        `carenagem lateral esculpida ${side}`,
        side * 0.67,
        [
          { z: -0.82, centerY: 0.46, halfWidth: 0.065, halfHeight: 0.13 },
          { z: -0.58, centerY: 0.48, halfWidth: 0.18, halfHeight: 0.19 },
          { z: 0.08, centerY: 0.45, halfWidth: 0.2, halfHeight: 0.2 },
          { z: 0.38, centerY: 0.42, halfWidth: 0.075, halfHeight: 0.135 },
        ],
        bodyPaint,
      ));
    }
    mergeParts("carenagens aerodinâmicas consolidadas", bodyworkParts, bodyPaint, "base", true);

    // Two restrained team-colored stripes identify each livery without real logos.
    const teamStripeParts: Mesh[] = [];
    for (const side of [-1, 1]) {
      teamStripeParts.push(makeTube(
        `filete dourado substituível do nariz ${side}`,
        [
          new Vector3(side * 0.105, 0.745, -0.36),
          new Vector3(side * 0.105, 0.785, 0.16),
          new Vector3(side * 0.082, 0.76, 0.83),
          new Vector3(side * 0.032, 0.68, 1.42),
        ],
        0.022,
        gold,
      ));
      teamStripeParts.push(makeTube(
        `friso lateral da equipe ${side}`,
        [
          new Vector3(side * 0.82, 0.57, -0.5),
          new Vector3(side * 0.85, 0.57, -0.12),
          new Vector3(side * 0.82, 0.55, 0.22),
        ],
        0.019,
        gold,
      ));
    }
    mergeParts("frisos de pintura da equipe", teamStripeParts, gold, "accent");

    const seat = MeshBuilder.CreateCapsule(
      "assento anatômico de competição",
      { radius: 0.27, height: 0.8, tessellation: 18 },
      this.scene,
    );
    seat.position = new Vector3(0, 0.64, -0.34);
    seat.scaling = new Vector3(0.92, 0.86, 0.72);
    addPart(seat, seatMaterial);

    const engine = MeshBuilder.CreateCylinder(
      "bloco do motor monocilíndrico",
      { height: 0.48, diameterTop: 0.43, diameterBottom: 0.36, tessellation: 24 },
      this.scene,
    );
    engine.position = new Vector3(0.43, 0.66, -0.98);
    addPart(engine, alloy);
    const finParts = [0.51, 0.61, 0.71].map((y) => {
      const fin = MeshBuilder.CreateTorus(
        `aleta de refrigeração ${y}`,
        { diameter: 0.42, thickness: 0.026, tessellation: 24 },
        this.scene,
      );
      fin.position = new Vector3(0.43, y, -0.98);
      fin.material = frameMetal;
      return fin;
    });
    mergeParts("aletas do motor consolidadas", finParts, frameMetal);
    const exhaust = addTube(
      "escapamento curvo em alumínio",
      [
        new Vector3(0.61, 0.72, -0.92),
        new Vector3(0.78, 0.69, -0.98),
        new Vector3(0.91, 0.62, -1.18),
        new Vector3(1.06, 0.62, -1.22),
      ],
      0.055,
      alloy,
    );
    exhaust.scaling.z = 1.08;

    // Four rounded slick tires, animated as a group with their alloy wheel details.
    for (const side of [-1, 1]) {
      for (const axle of [
        { label: "dianteiro", z: 0.82 },
        { label: "traseiro", z: -1.04 },
      ]) {
        const center = new Vector3(side * 0.96, 0.39, axle.z);
        const tire = MeshBuilder.CreateTorus(
          `pneu ${axle.label} ${side}`,
          { diameter: 0.7, thickness: 0.145, tessellation: 32 },
          this.scene,
        );
        tire.position.copyFrom(center);
        tire.rotation.y = Math.PI / 2;
        tire.metadata = { stintoriaWheelComponent: true };
        addPart(tire, rubber);

        const rim = MeshBuilder.CreateTorus(
          `aro escovado ${axle.label} ${side}`,
          { diameter: 0.43, thickness: 0.026, tessellation: 24 },
          this.scene,
        );
        rim.position = new Vector3(side * 1.015, center.y, center.z);
        rim.rotation.y = Math.PI / 2;
        rim.metadata = { stintoriaWheelComponent: true };
        addPart(rim, alloy);

        const hub = MeshBuilder.CreateCylinder(
          `cubo de roda ${axle.label} ${side}`,
          { height: 0.16, diameter: 0.19, tessellation: 18 },
          this.scene,
        );
        hub.position = new Vector3(side * 1.035, center.y, center.z);
        hub.rotation.z = Math.PI / 2;
        hub.metadata = { stintoriaWheelComponent: true };
        addPart(hub, gold);
      }
    }

    const steering = MeshBuilder.CreateTorus(
      "volante tubular esportivo",
      { diameter: 0.43, thickness: 0.045, tessellation: 28 },
      this.scene,
    );
    steering.position = new Vector3(0, 1.21, 0.31);
    steering.rotation.x = 0.22;
    addPart(steering, carbon);
    addTube(
      "coluna de direção metálica",
      [new Vector3(0, 0.63, 0.47), new Vector3(0, 0.92, 0.38), new Vector3(0, 1.18, 0.31)],
      0.035,
      alloy,
    );

    const driverBody = MeshBuilder.CreateCapsule(
      "piloto com macacão",
      { radius: 0.2, height: 0.68, tessellation: 16 },
      this.scene,
    );
    driverBody.position = new Vector3(0, 1.03, -0.35);
    driverBody.scaling = new Vector3(0.82, 0.96, 0.65);
    const driverParts = [driverBody];
    for (const side of [-1, 1]) {
      driverParts.push(makeTube(
        `braço do piloto ${side}`,
        [
          new Vector3(side * 0.16, 1.2, -0.28),
          new Vector3(side * 0.22, 1.13, -0.02),
          new Vector3(side * 0.18, 1.1, 0.24),
        ],
        0.067,
        carbon,
      ));
    }
    mergeParts("piloto e braços consolidados", driverParts, carbon, "base", true);

    const helmet = MeshBuilder.CreateSphere(
      "capacete integral",
      { diameter: 0.53, segments: 24 },
      this.scene,
    );
    helmet.position = new Vector3(0, 1.57, -0.16);
    helmet.scaling = new Vector3(0.91, 1.02, 1.08);
    addPart(helmet, carbon, "base", true);

    const visor = MeshBuilder.CreateSphere(
      "viseira fumê curva",
      { diameter: 0.4, segments: 18 },
      this.scene,
    );
    visor.position = new Vector3(0, 1.58, 0.055);
    visor.scaling = new Vector3(0.86, 0.42, 0.32);
    addPart(visor, visorMaterial);
  }

  private makeTvCamera(): ArcRotateCamera {
    const camera = new ArcRotateCamera(
      "câmera TV",
      Math.PI * 0.72,
      0.98,
      35,
      Vector3.Zero(),
      this.scene,
    );
    camera.lowerRadiusLimit = 22;
    camera.upperRadiusLimit = 60;
    camera.lowerBetaLimit = 0.45;
    camera.upperBetaLimit = 1.42;
    camera.wheelPrecision = 45;
    return camera;
  }

  private makeAerialCamera(): ArcRotateCamera {
    const camera = new ArcRotateCamera(
      "câmera aérea",
      -Math.PI / 2,
      1.18,
      105,
      this.gridCenter,
      this.scene,
    );
    camera.lowerRadiusLimit = 72;
    camera.upperRadiusLimit = 165;
    camera.lowerBetaLimit = 0.18;
    camera.upperBetaLimit = 1.42;
    camera.wheelPrecision = 55;
    return camera;
  }

  private makePilotCamera(): UniversalCamera {
    const camera = new UniversalCamera(
      "câmera de acompanhamento do piloto",
      new Vector3(0, 1.86, -3.8),
      this.scene,
    );
    camera.parent = this.kartRoot;
    camera.minZ = 0.06;
    camera.fov = 0.88;
    camera.inputs.clear();
    return camera;
  }

  private makeTransitionCamera(): UniversalCamera {
    const camera = new UniversalCamera(
      "câmera de transição cinematográfica",
      new Vector3(0, 16, -45),
      this.scene,
    );
    camera.minZ = 0.06;
    camera.inputs.clear();
    return camera;
  }

  private teamPaintMaterial(teamId: string, teamColor: string): PBRMaterial {
    const existing = this.teamPaintMaterials.get(teamId);
    if (existing) return existing;
    const material = surfaceMaterial(
      this.scene,
      `pintura ${teamId}`,
      teamColor,
      0.38,
      0.12,
    );
    this.teamPaintMaterials.set(teamId, material);
    return material;
  }

  private teamAccentMaterial(teamId: string, teamColor: string): PBRMaterial {
    const existing = this.teamAccentMaterials.get(teamId);
    if (existing) return existing;
    const base = color(teamColor);
    const highlight = new Color3(
      base.r * 0.7 + 0.3,
      base.g * 0.7 + 0.3,
      base.b * 0.7 + 0.3,
    );
    const material = new PBRMaterial(`filete da equipe ${teamId}`, this.scene);
    material.albedoColor = highlight;
    material.roughness = 0.44;
    material.metallic = 0.2;
    material.environmentIntensity = 0.38;
    this.teamAccentMaterials.set(teamId, material);
    return material;
  }

  private applyTeamPaint(meshes: readonly Mesh[], slot: KartGridSlot): void {
    const material = this.teamPaintMaterial(slot.teamId, slot.teamColor);
    const accent = this.teamAccentMaterial(slot.teamId, slot.teamColor);
    for (const mesh of meshes) {
      if (this.paintableKartMeshes.has(mesh)) mesh.material = material;
      else if (this.teamAccentKartMeshes.has(mesh)) mesh.material = accent;
    }
  }

  private addDriverLabel(slot: KartGridSlot): void {
    const plane = MeshBuilder.CreatePlane(
      `identificação ${slot.driverId}`,
      { width: 3.1, height: 0.9 },
      this.scene,
    );
    plane.position = new Vector3(slot.x, 2.9, slot.z);
    plane.billboardMode = Mesh.BILLBOARDMODE_ALL;

    const texture = new DynamicTexture(
      `textura identificação ${slot.driverId}`,
      { width: 512, height: 128 },
      this.scene,
      true,
    );
    texture.hasAlpha = true;
    texture.drawText(
      slot.name.slice(0, 34),
      16,
      43,
      "bold 22px Arial",
      "#f4f1e8",
      "rgba(15, 21, 27, 0.76)",
      true,
    );
    texture.drawText(
      slot.teamName.slice(0, 38),
      16,
      86,
      "16px Arial",
      slot.teamColor,
      null,
      true,
    );

    const material = new StandardMaterial(
      `material identificação ${slot.driverId}`,
      this.scene,
    );
    material.diffuseTexture = texture;
    material.opacityTexture = texture;
    material.useAlphaFromDiffuseTexture = true;
    material.disableLighting = true;
    material.backFaceCulling = false;
    plane.material = material;
    this.gridLabels.push({ driverId: slot.driverId, plane, material, texture });
  }

  private addDriverNumberPlate(slot: KartGridSlot): void {
    const plane = MeshBuilder.CreatePlane(
      `identificador no nariz ${slot.driverId}`,
      { width: 0.54, height: 0.24 },
      this.scene,
    );
    plane.position = new Vector3(slot.x + 0.58, 0.82, slot.z);
    plane.rotation = new Vector3(-Math.PI / 2, slot.yaw, 0);
    plane.isPickable = false;

    const texture = new DynamicTexture(
      `textura identificador ${slot.driverId}`,
      { width: 256, height: 112 },
      this.scene,
      true,
    );
    texture.hasAlpha = false;
    texture.drawText(
      slot.driverId.toUpperCase(),
      12,
      78,
      "bold 48px Arial",
      "#f5f2e9",
      slot.teamColor,
      true,
    );

    const material = new StandardMaterial(`placa ${slot.driverId}`, this.scene);
    material.diffuseTexture = texture;
    material.emissiveColor = new Color3(0.14, 0.14, 0.14);
    material.disableLighting = true;
    material.backFaceCulling = false;
    plane.material = material;
    this.driverNumberPlates.push({ driverId: slot.driverId, plane, material, texture });
  }

  private updateRacePresentation(deltaMs: number): void {
    this.replay.advance(Math.max(0, Math.min(deltaMs, 100)));
    const totalLaps = this.replay.getState().totalSectors / 3;
    for (const [driverId, root] of this.kartRoots) {
      const lapProgress = this.replay.lapProgressForDriver(driverId);
      if (lapProgress === null) continue;
      const startingOffset = this.visualStartLaps.get(driverId) ?? 0;
      const progressRatio = totalLaps > 0
        ? Math.max(0, Math.min(1, lapProgress / totalLaps))
        : 0;
      const fadedStartOffset = startingOffset * (1 - progressRatio);
      const pose = trackPoseAtLapProgress(
        lapProgress + fadedStartOffset,
        this.visualLaneOffsets.get(driverId) ?? 0,
      );

      const previousX = root.position.x;
      const previousZ = root.position.z;
      root.position.set(pose.x, 0.05, pose.z);
      root.rotation.y = pose.yaw;
      const distance = Math.hypot(pose.x - previousX, pose.z - previousZ);
      if (distance > 0) {
        for (const wheel of this.wheelsByDriver.get(driverId) ?? []) {
          wheel.rotation.x += distance / 0.33;
        }
      }
    }

    for (const label of this.gridLabels) {
      const root = this.kartRoots.get(label.driverId);
      if (root) label.plane.position.set(root.position.x, root.position.y + 2.9, root.position.z);
    }
    for (const plate of this.driverNumberPlates) {
      const root = this.kartRoots.get(plate.driverId);
      if (!root) continue;
      const forwardX = Math.sin(root.rotation.y);
      const forwardZ = Math.cos(root.rotation.y);
      plate.plane.position.set(
        root.position.x + forwardX * 0.58,
        root.position.y + 0.82,
        root.position.z + forwardZ * 0.58,
      );
      plate.plane.rotation.set(-Math.PI / 2, root.rotation.y, 0);
    }

    if (this.mode === "tv") {
      const selectedProgress = this.replay.lapProgressForDriver(this.selectedDriverId);
      const selectedRoot = this.selectedRoot();
      if (selectedProgress !== null) {
        const laneOffset = this.visualLaneOffsets.get(this.selectedDriverId) ?? 0;
        const before = trackPoseAtLapProgress(selectedProgress - 0.018, laneOffset);
        const ahead = trackPoseAtLapProgress(selectedProgress + 0.018, laneOffset);
        const headingDelta = ahead.yaw - before.yaw;
        const turnAmount = Math.abs(Math.atan2(Math.sin(headingDelta), Math.cos(headingDelta)));
        const cornerShot = turnAmount > 0.19;
        const desiredRadius = cornerShot ? 32 : 46;
        const desiredBeta = cornerShot ? 0.98 : 0.84;
        const nextPose = trackPoseAtLapProgress(selectedProgress + 0.025, laneOffset);
        const anticipatedTarget = Vector3.Lerp(
          selectedRoot.position,
          new Vector3(nextPose.x, selectedRoot.position.y + 0.2, nextPose.z),
          0.45,
        );
        const forward = new Vector3(
          Math.sin(selectedRoot.rotation.y),
          0,
          Math.cos(selectedRoot.rotation.y),
        );
        anticipatedTarget.addInPlace(forward.scale(1.8));
        const blend = 1 - Math.exp(-Math.min(deltaMs, 100) / 240);
        const currentTarget = this.tvCamera.getTarget();
        this.tvCamera.setTarget(Vector3.Lerp(currentTarget, anticipatedTarget, blend));
        this.tvCamera.radius += (desiredRadius - this.tvCamera.radius) * blend;
        this.tvCamera.beta += (desiredBeta - this.tvCamera.beta) * blend;
        const desiredAlpha = this.tvAngleForHeading(selectedRoot.rotation.y);
        const angleDelta = Math.atan2(
          Math.sin(desiredAlpha - this.tvCamera.alpha),
          Math.cos(desiredAlpha - this.tvCamera.alpha),
        );
        this.tvCamera.alpha += angleDelta * blend;
      } else {
        const target = selectedRoot.position;
        const blend = 1 - Math.exp(-Math.min(deltaMs, 100) / 240);
        this.tvCamera.setTarget(
          Vector3.Lerp(this.tvCamera.getTarget(), target, blend),
        );
      }
    }
    this.updateCameraTransition(deltaMs);
  }

  private restoreGridFormation(): void {
    for (const [driverId, start] of this.gridStarts) {
      const root = this.kartRoots.get(driverId);
      if (!root) continue;
      root.position.set(start.x, 0.05, start.z);
      root.rotation.y = start.yaw;
      for (const wheel of this.wheelsByDriver.get(driverId) ?? []) wheel.rotation.x = 0;
    }
  }

  private selectedRoot(): TransformNode {
    return this.kartRoots.get(this.selectedDriverId) ?? this.kartRoot;
  }

  private clearGrid(): void {
    delete this.canvas.dataset.stintoriaGridCount;
    delete this.canvas.dataset.stintoriaGridPartCount;
    delete this.canvas.dataset.stintoriaGridDriverIds;
    delete this.canvas.dataset.stintoriaGridDriverTeamIds;
    delete this.canvas.dataset.stintoriaKartSourceParts;
    delete this.canvas.dataset.stintoriaKartWheelCountPerDriver;
    delete this.canvas.dataset.stintoriaWheelAnimationMeshesPerDriver;
    delete this.canvas.dataset.stintoriaNumberPlateCount;
    delete this.canvas.dataset.stintoriaNumberPlateDriverIds;
    delete this.canvas.dataset.stintoriaTeamMaterialCount;
    delete this.canvas.dataset.stintoriaSceneMeshCount;
    delete this.canvas.dataset.stintoriaSceneMaterialCount;
    delete this.canvas.dataset.stintoriaShadowCasterCount;
    delete this.canvas.dataset.stintoriaSelectedDriverId;
    delete this.canvas.dataset.stintoriaReplayTimingDrivers;
    for (const clone of this.gridClones) clone.dispose(false, false);
    this.gridClones.length = 0;
    this.wheelsByDriver.clear();
    this.gridStarts.clear();
    this.visualStartLaps.clear();
    this.visualLaneOffsets.clear();
    this.pilotCamera.parent = this.kartRoot;
    this.selectedDriverId = "";
    for (const [driverId, root] of this.kartRoots) {
      if (root !== this.kartRoot) root.dispose(true);
    }
    this.kartRoots.clear();
    for (const label of this.gridLabels) {
      label.plane.dispose(false, false);
      label.material.dispose(false, false);
      label.texture.dispose();
    }
    this.gridLabels.length = 0;
    for (const plate of this.driverNumberPlates) {
      plate.plane.dispose(false, false);
      plate.material.dispose(false, false);
      plate.texture.dispose();
    }
    this.driverNumberPlates.length = 0;
    for (const material of this.teamPaintMaterials.values()) {
      material.dispose(false, false);
    }
    this.teamPaintMaterials.clear();
    for (const material of this.teamAccentMaterials.values()) {
      material.dispose(false, false);
    }
    this.teamAccentMaterials.clear();
    this.protagonistId = "";
  }

  private makeShadows(): ShadowGenerator {
    const light = this.scene.lights.find(
      (candidate) => candidate instanceof DirectionalLight,
    ) as DirectionalLight;
    const shadows = new ShadowGenerator(1024, light);
    shadows.useBlurExponentialShadowMap = true;
    shadows.blurKernel = 20;
    for (const mesh of this.kartShadowMeshes) shadows.addShadowCaster(mesh);
    return shadows;
  }

  private configureShadows(): void {
    for (const mesh of this.scene.meshes) {
      if (
        mesh instanceof Mesh &&
        this.kartMeshes.includes(mesh) &&
        !this.kartShadowMeshes.has(mesh)
      ) {
        mesh.receiveShadows = false;
      } else if (mesh.name !== "terreno do circuito" && mesh.name !== "pista fechada") {
        mesh.receiveShadows = true;
      }
    }
  }

}
