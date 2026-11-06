import { useEffect, useRef, useState } from 'react';
import {
  ArrowLeft,
  BadgeInfo,
  CheckCircle2,
  ChevronRight,
  CircleDashed,
  Flag,
  LoaderCircle,
  Save,
  ShieldCheck,
} from 'lucide-react';
import {
  CareerBridgeError,
  requestCareer,
  type CareerCatalog,
  type CareerOrigin,
  type CareerPace,
  type CareerReply,
  type CareerRaceSnapshot,
  type CareerSnapshot,
  type CareerTraining,
} from './careerBridgeClient';
import {
  BrowserCareerCheckpointStorage,
  CareerStorageError,
  IndexedDbCareerCheckpointAdapter,
} from './careerCheckpointStorage';
import { StintoriaStage } from './StintoriaStage';
import './career-experience.css';

type Screen = 'home' | 'new' | 'career' | 'race';
type StorageState = 'checking' | 'empty' | 'ready' | 'recovered' | 'error';
type OriginOption = { value: CareerOrigin; label: string };
type PendingRaceCommit = {
  reply: CareerReply;
  expectedRevision: number;
  kind: 'start' | 'advance';
};

const originLabels: Record<CareerOrigin, string> = {
  FamiliaHumilde: 'Família humilde',
  LigadaAoEsporte: 'Ligada ao esporte',
  Empresaria: 'Empresária',
  SimRacer: 'Sim racer',
};
const statusLabels: Record<string, string> = {
  NaoDisputada: 'Não disputada',
  Preparada: 'Preparada',
  Concluida: 'Concluída',
};
const trainingLabels: Record<CareerTraining, string> = {
  Descanso: 'Descanso',
  Tecnico: 'Treinamento técnico',
};

function downloadRawCareer(raw: string) {
  const url = URL.createObjectURL(new Blob([raw], { type: 'application/json;charset=utf-8' }));
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = 'stintoria-carreira-recuperacao.json';
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1_000);
}

export function CareerExperience({ onOpenDemo }: { onOpenDemo: () => void }) {
  const [screen, setScreen] = useState<Screen>('home');
  const [catalog, setCatalog] = useState<CareerCatalog | null>(null);
  const [storageState, setStorageState] = useState<StorageState>('checking');
  const [career, setCareer] = useState<CareerSnapshot | null>(null);
  const [raceSnapshot, setRaceSnapshot] = useState<CareerRaceSnapshot | null>(null);
  const [pendingRaceCommit, setPendingRaceCommit] = useState<PendingRaceCommit | null>(null);
  const [checkpoint, setCheckpoint] = useState<string | null>(null);
  const [checkpointVersion, setCheckpointVersion] = useState<number | null>(null);
  const [revision, setRevision] = useState<number | null>(null);
  const [name, setName] = useState('');
  const [nationality, setNationality] = useState('Brasil');
  const [origin, setOrigin] = useState<CareerOrigin | ''>('');
  const [teamId, setTeamId] = useState('');
  const [seedText, setSeedText] = useState('42');
  const [training, setTraining] = useState<CareerTraining>('Descanso');
  const [pace, setPace] = useState<CareerPace>('Equilibrado');
  const [promotionTeamId, setPromotionTeamId] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [storageMessage, setStorageMessage] = useState('Validando carreira local pelo C#…');
  const rawRecovery = useRef<string | null>(null);
  const storageRef = useRef<BrowserCareerCheckpointStorage | null>(null);
  const runningRef = useRef(false);

  if (storageRef.current === null) {
    storageRef.current = new BrowserCareerCheckpointStorage(new IndexedDbCareerCheckpointAdapter());
  }

  useEffect(() => {
    let mounted = true;
    void (async () => {
      try {
        const loaded = await storageRef.current!.inspect();
        if (!mounted) return;
        rawRecovery.current = loaded.rawJson;
        if (loaded.status === 'empty') {
          setStorageState('empty');
          setStorageMessage(loaded.message);
          return;
        }
        if (loaded.status === 'corrupt' || loaded.status === 'incompatible') {
          setStorageState('error');
          setStorageMessage(loaded.message);
          return;
        }
        const candidate = loaded.candidates[0];
        if (!candidate) throw new CareerBridgeError('O IndexedDB não contém uma cópia verificável da carreira.');
        const reply = await requestCareer({ operation: 'load', checkpoint: candidate.checkpoint });
        if (!reply.career || reply.checkpoint !== candidate.checkpoint ||
            reply.checkpointFormatVersion !== candidate.checkpointVersion) {
          throw new CareerBridgeError('O motor C# não confirmou os mesmos bytes e a mesma versão do save.');
        }
        if (!mounted) return;
        setCareer(reply.career);
        setPromotionTeamId(reply.career.progression.offeredTeams[0]?.id ?? '');
        setRaceSnapshot(reply.race ?? null);
        setCheckpoint(candidate.checkpoint);
        setCheckpointVersion(candidate.checkpointVersion);
        setRevision(loaded.revision);
        setStorageState(loaded.status === 'recovered' ? 'recovered' : 'ready');
        setStorageMessage(loaded.status === 'recovered'
          ? 'Uma cópia A/B foi recuperada e validada pelo C#; a cópia anterior foi preservada.'
          : 'Save da carreira validado pelo C#.');
      } catch (cause) {
        if (!mounted) return;
        setStorageState('error');
        setStorageMessage(cause instanceof Error ? cause.message : 'Não foi possível validar a carreira local.');
      }
    })();
    return () => { mounted = false; };
  }, []);

  const runBusy = async (work: () => Promise<void>) => {
    if (runningRef.current) return;
    runningRef.current = true;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await work();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Não foi possível concluir a operação.');
    } finally {
      runningRef.current = false;
      setBusy(false);
    }
  };

  const persistRaceReply = async (
    reply: CareerReply,
    expectedRevision: number,
    kind: PendingRaceCommit['kind'],
  ) => {
    if (!reply.career || !reply.race || !reply.checkpoint) {
      throw new CareerBridgeError('O C# não retornou carreira, corrida e checkpoint completos.');
    }
    const pending = { reply, expectedRevision, kind };
    setPendingRaceCommit(pending);
    let saved;
    try {
      saved = await storageRef.current!.save(
        reply.checkpoint,
        reply.checkpointFormatVersion,
        expectedRevision,
      );
    } catch {
      throw new CareerBridgeError(
        'O motor respondeu, mas a gravação local não foi confirmada. Não avance novamente; tente salvar este mesmo checkpoint ou recarregue a carreira para conferir o IndexedDB.',
      );
    }
    setPendingRaceCommit(null);
    setCareer(reply.career);
    setPromotionTeamId(reply.career.progression.offeredTeams[0]?.id ?? '');
    setRaceSnapshot(reply.race);
    setCheckpoint(reply.checkpoint);
    setCheckpointVersion(reply.checkpointFormatVersion);
    setRevision(saved.revision);
    setStorageState('ready');
    setStorageMessage('Checkpoint da corrida confirmado pelo IndexedDB.');
    setNotice(kind === 'start'
      ? 'Corrida oficial iniciada pelo C# e checkpoint confirmado. Nenhum setor foi avançado na largada.'
      : reply.race.finished
        ? 'Bandeirada confirmada pelo C#. Classificação e pontos oficiais foram gravados uma vez.'
        : `Setor ${reply.race.completedSectors} confirmado pelo C# e salvo; a posição 3D continua aproximada.`);
    setScreen('race');
  };

  const retryPendingRaceSave = () => void runBusy(async () => {
    const pending = pendingRaceCommit;
    if (!pending) throw new CareerBridgeError('Não há checkpoint de corrida pendente para confirmar.');
    await persistRaceReply(pending.reply, pending.expectedRevision, pending.kind);
  });

  const openNewCareer = () => void runBusy(async () => {
    if (storageState !== 'empty') {
      throw new Error('O espaço de carreira local está ocupado ou precisa de validação. Nenhum save será substituído.');
    }
    const reply = await requestCareer({ operation: 'catalog' });
    setCatalog(reply.catalog!);
    setTeamId(reply.catalog!.teams[0]?.id ?? '');
    setOrigin(reply.catalog!.origins[0] ?? '');
    setScreen('new');
  });

  const createCareer = () => void runBusy(async () => {
    if (!catalog || !origin || !teamId) throw new Error('Carregue o catálogo C# e escolha origem e equipe.');
    const parsedSeed = Number(seedText);
    if (!Number.isInteger(parsedSeed) || parsedSeed < 0 || parsedSeed > 4_294_967_295) {
      throw new Error('A seed deve ser um inteiro entre 0 e 4.294.967.295.');
    }
    const reply = await requestCareer({
      operation: 'create',
      seed: parsedSeed,
      name,
      nationality,
      origin,
      teamId,
    });
    if (!reply.career || !reply.checkpoint) throw new CareerBridgeError('O C# não retornou uma carreira criada.');
    const saved = await storageRef.current!.save(reply.checkpoint, reply.checkpointFormatVersion, null);
    setCareer(reply.career);
    setPromotionTeamId(reply.career.progression.offeredTeams[0]?.id ?? '');
    setRaceSnapshot(reply.race ?? null);
    setCheckpoint(reply.checkpoint);
    setCheckpointVersion(reply.checkpointFormatVersion);
    setRevision(saved.revision);
    setStorageState('ready');
    setStorageMessage('Carreira criada pelo C# e gravação confirmada no IndexedDB.');
    setNotice('Piloto e temporada confirmados pelo motor C#. O save local foi confirmado.');
    setScreen('career');
  });

  const continueCareer = () => void runBusy(async () => {
    const loaded = await storageRef.current!.inspect();
    if (loaded.status !== 'ready' && loaded.status !== 'recovered') {
      throw new CareerStorageError(loaded.status === 'incompatible' ? 'incompatible' : 'corrupt', loaded.message);
    }
    const candidate = loaded.candidates[0];
    if (!candidate) throw new CareerStorageError('corrupt', 'Não há checkpoint válido para continuar.');
    const reply = await requestCareer({ operation: 'load', checkpoint: candidate.checkpoint });
    if (!reply.career || reply.checkpoint !== candidate.checkpoint ||
        reply.checkpointFormatVersion !== candidate.checkpointVersion) {
      throw new CareerBridgeError('O motor C# não confirmou o save selecionado.');
    }
    setCareer(reply.career);
    setPromotionTeamId(reply.career.progression.offeredTeams[0]?.id ?? '');
    setRaceSnapshot(reply.race ?? null);
    setPendingRaceCommit(null);
    setCheckpoint(candidate.checkpoint);
    setCheckpointVersion(candidate.checkpointVersion);
    setRevision(loaded.revision);
    setStorageState(loaded.status === 'recovered' ? 'recovered' : 'ready');
    setStorageMessage('Carreira retomada a partir do checkpoint confirmado pelo C#.');
    setNotice('Carreira carregada sem avançar etapa ou alterar resultados.');
    setScreen(reply.race && !reply.race.finished ? 'race' : 'career');
  });

  const saveAndContinue = () => void runBusy(async () => {
    if (pendingRaceCommit) {
      throw new CareerBridgeError('Confirme o checkpoint de corrida pendente antes de salvar novamente.');
    }
    if (!checkpoint || checkpointVersion === null) throw new Error('A carreira ainda não tem checkpoint C#.');
    const reply = await requestCareer({ operation: 'load', checkpoint });
    if (!reply.career || reply.checkpoint !== checkpoint ||
        reply.checkpointFormatVersion !== checkpointVersion) {
      throw new CareerBridgeError('O C# não confirmou o checkpoint atual. A gravação não foi declarada.');
    }
    const saved = await storageRef.current!.save(checkpoint, checkpointVersion, revision);
    setCareer(reply.career);
    setRaceSnapshot(reply.race ?? null);
    setRevision(saved.revision);
    setStorageState('ready');
    setStorageMessage('Gravação da carreira confirmada pelo IndexedDB após validação C#.');
    setNotice(saved.alreadySaved ? 'Este checkpoint já estava salvo; nenhuma etapa foi repetida.' : 'Carreira salva e confirmada.');
  });

  const prepareCareer = () => void runBusy(async () => {
    if (pendingRaceCommit) {
      throw new CareerBridgeError('Confirme o checkpoint de corrida pendente antes de alterar a preparação.');
    }
    if (!career || !checkpoint || checkpointVersion === null || revision === null)
      throw new CareerBridgeError('Carregue e valide a carreira antes de confirmar a preparação.');
    const option = career.preparationOptions.find(item => item.training === training);
    if (!option?.available) {
      throw new CareerBridgeError(option?.unavailableReason ?? 'Esta preparação não está disponível para a etapa atual.');
    }

    const reply = await requestCareer({
      operation: 'prepare',
      checkpoint,
      expectedRound: career.nextRound,
      training,
      pace,
    });
    if (!reply.career || !reply.checkpoint ||
        reply.career.currentPreparation?.round !== career.nextRound) {
      throw new CareerBridgeError('O C# não confirmou a preparação da próxima etapa.');
    }
    const saved = await storageRef.current!.save(
      reply.checkpoint,
      reply.checkpointFormatVersion,
      revision,
    );
    setCareer(reply.career);
    setRaceSnapshot(reply.race ?? null);
    setCheckpoint(reply.checkpoint);
    setCheckpointVersion(reply.checkpointFormatVersion);
    setRevision(saved.revision);
    setStorageState('ready');
    setStorageMessage('Preparação confirmada pelo C# e checkpoint gravado no IndexedDB.');
    setNotice(saved.alreadySaved
      ? 'A preparação já estava salva; nenhum crédito foi consumido novamente.'
      : 'Preparação oficial confirmada e salva para a próxima etapa.');
  });

  const acceptPromotion = () => void runBusy(async () => {
    if (!career?.progression.offerAvailable || !checkpoint || revision === null) {
      throw new CareerBridgeError('Não há uma oferta de promoção pendente para esta carreira.');
    }
    if (!promotionTeamId) throw new CareerBridgeError('Escolha uma equipe F4 oferecida pelo motor C#.');
    const reply = await requestCareer({ operation: 'acceptPromotion', checkpoint, teamId: promotionTeamId });
    if (!reply.career || !reply.checkpoint ||
        reply.career.progression.decision !== 'Accepted' ||
        reply.career.progression.currentCategoryId !== 'f4' ||
        reply.career.progression.currentTeamId !== promotionTeamId) {
      throw new CareerBridgeError('O C# não confirmou a promoção e a equipe F4 escolhida.');
    }
    const saved = await storageRef.current!.save(
      reply.checkpoint, reply.checkpointFormatVersion, revision);
    setCareer(reply.career);
    setRaceSnapshot(reply.race ?? null);
    setCheckpoint(reply.checkpoint);
    setCheckpointVersion(reply.checkpointFormatVersion);
    setRevision(saved.revision);
    setStorageState('ready');
    setStorageMessage('Promoção F4 confirmada pelo C# e pelo IndexedDB.');
    setNotice('Promoção aceita. A temporada F4 foi criada; suas corridas aguardam o motor próprio.');
  });

  const declinePromotion = () => void runBusy(async () => {
    if (!career?.progression.offerAvailable || !checkpoint || revision === null) {
      throw new CareerBridgeError('Não há uma oferta de promoção pendente para esta carreira.');
    }
    const reply = await requestCareer({ operation: 'declinePromotion', checkpoint });
    if (!reply.career || !reply.checkpoint ||
        reply.career.progression.decision !== 'Declined' ||
        reply.career.progression.currentCategoryId !== 'kart') {
      throw new CareerBridgeError('O C# não confirmou a recusa da oferta.');
    }
    const saved = await storageRef.current!.save(
      reply.checkpoint, reply.checkpointFormatVersion, revision);
    setCareer(reply.career);
    setRaceSnapshot(reply.race ?? null);
    setCheckpoint(reply.checkpoint);
    setCheckpointVersion(reply.checkpointFormatVersion);
    setRevision(saved.revision);
    setStorageState('ready');
    setStorageMessage('Decisão de carreira confirmada pelo C# e pelo IndexedDB.');
    setNotice('Oferta recusada e registrada. A temporada Kart concluída permanece preservada.');
  });

  const startCareerRace = () => void runBusy(async () => {
    if (!career || !checkpoint || checkpointVersion === null || revision === null)
      throw new CareerBridgeError('Carregue e valide a carreira antes de iniciar a corrida.');
    if (career.finished) throw new CareerBridgeError('A temporada Kart já foi encerrada pelo C#.');
    if (!career.prepared) {
      throw new CareerBridgeError('Confirme a preparação da etapa antes de ir à corrida.');
    }
    if (raceSnapshot && !raceSnapshot.finished && raceSnapshot.round === career.nextRound) {
      throw new CareerBridgeError('A corrida já está ativa; retome a sessão existente.');
    }

    const reply = await requestCareer({
      operation: 'startRace',
      checkpoint,
      expectedRound: career.nextRound,
    });
    if (!reply.career || !reply.race || !reply.checkpoint ||
        reply.race.round !== career.nextRound || reply.race.finished ||
        reply.race.classification.length !== 20) {
      throw new CareerBridgeError('O C# não confirmou a sessão oficial de 20 pilotos.');
    }
    await persistRaceReply(reply, revision, 'start');
  });

  const advanceCareerRace = (selectedPace: CareerPace) => void runBusy(async () => {
    if (!career || !raceSnapshot || !checkpoint ||
        checkpointVersion === null || revision === null) {
      throw new CareerBridgeError('Carregue a sessão e o checkpoint antes de avançar.');
    }
    if (pendingRaceCommit) {
      throw new CareerBridgeError('Confirme ou recarregue o checkpoint pendente antes de avançar outro setor.');
    }
    if (raceSnapshot.finished || raceSnapshot.round !== career.nextRound) {
      throw new CareerBridgeError('Não há uma corrida ativa desta etapa para avançar.');
    }
    const expectedSector = raceSnapshot.completedSectors + 1;
    if (expectedSector > raceSnapshot.progress.totalSectors) {
      throw new CareerBridgeError('Todos os setores já foram confirmados pelo motor.');
    }

    const reply = await requestCareer({
      operation: 'advanceRace',
      checkpoint,
      expectedRound: raceSnapshot.round,
      expectedSector,
      pace: selectedPace,
    });
    if (!reply.career || !reply.race || !reply.checkpoint ||
        reply.race.completedSectors !== expectedSector) {
      throw new CareerBridgeError('O C# não confirmou exatamente o próximo setor; o checkpoint anterior foi preservado.');
    }
    await persistRaceReply(reply, revision, 'advance');
  });

  const origins: OriginOption[] = catalog?.origins
    .filter((value): value is CareerOrigin => value in originLabels)
    .map(value => ({ value, label: originLabels[value] })) ?? [];
  const canCreate = storageState === 'empty' && !busy;
  const selectedTraining = career?.preparationOptions.find(option => option.training === training);
  const activeCareerRace = career && raceSnapshot?.round === career.nextRound && !raceSnapshot.finished
    ? raceSnapshot
    : null;

  if (screen === 'race' && career && raceSnapshot) {
    const circuitName = career.calendar.find(round => round.number === raceSnapshot.round)?.circuitName ??
      'Circuito Kart';
    return (
      <StintoriaStage
        onBack={() => setScreen('career')}
        careerRace={{
          race: raceSnapshot,
          circuitName,
          busy,
          pendingSave: pendingRaceCommit !== null,
          error,
          notice,
          onAdvance: advanceCareerRace,
          onRetrySave: retryPendingRaceSave,
        }}
      />
    );
  }

  return (
    <main className="career-app">
      <header className="career-topbar">
        <a className="career-brand" href="#" onClick={event => { event.preventDefault(); setScreen('home'); }}>
          <span className="career-brand-mark">S</span>
          <span><strong>STINTORIA</strong><small>Carreira no kart</small></span>
        </a>
        <span className="career-edition"><span /> EXPERIÊNCIA INDIVIDUAL</span>
      </header>

      <div className="career-shell">
        {screen !== 'home' && (
          <button type="button" className="career-back" onClick={() => setScreen(screen === 'career' ? 'home' : 'home')}>
            <ArrowLeft size={15} /> Voltar ao menu
          </button>
        )}

        {screen === 'home' && (
          <>
            <section className="career-hero">
              <div>
                <span className="career-eyebrow"><i /> CARREIRA KART · TEMPORADA NACIONAL</span>
                <h1>Seu caminho começa<br /><em>na pista.</em></h1>
                <p>Crie um piloto, escolha uma equipe do elenco oficial e acompanhe a temporada Kart pelo motor C#.</p>
              </div>
              <div className="career-seal"><Flag size={30} /><span>STINTORIA</span><small>MOTORSPORT PRESTIGE</small></div>
            </section>

            <section className="career-menu-grid" aria-label="Menu inicial">
              <article className="career-menu-card career-menu-primary">
                <span className="career-card-kicker">COMEÇAR</span>
                <h2>Nova carreira</h2>
                <p>Cadastre seu piloto e confirme uma equipe elegível pelo núcleo C#.</p>
                <button type="button" className="career-button career-button-gold"
                  disabled={!canCreate} onClick={openNewCareer} data-testid="button-new-career">
                  {busy ? <LoaderCircle className="career-spin" size={16} /> : <ChevronRight size={16} />}
                  {storageState === 'checking'
                    ? 'Verificando save…'
                    : storageState === 'empty'
                      ? 'Criar carreira'
                      : storageState === 'error'
                        ? 'Armazenamento indisponível'
                        : 'Save existente preservado'}
                </button>
              </article>
              <article className="career-menu-card">
                <span className="career-card-kicker">RETOMAR</span>
                <h2>Continuar carreira</h2>
                <p>
                  {career
                    ? `${career.profile.name} · ${career.completedRounds}/${career.totalRounds} etapas concluídas`
                    : 'Valida o checkpoint local novamente no motor C# antes de abrir a temporada.'}
                </p>
                <button type="button" className="career-button career-button-outline"
                  disabled={busy || !career || storageState === 'error'} onClick={continueCareer}
                  data-testid="button-continue-career">
                  {busy ? <LoaderCircle className="career-spin" size={16} /> : <ArrowLeft size={16} />}
                  {storageState === 'checking' ? 'Validando…' : 'Continuar carreira'}
                </button>
              </article>
              <article className="career-menu-card career-menu-demo">
                <span className="career-card-kicker">AMBIENTE SEPARADO</span>
                <h2>Demonstração de corrida</h2>
                <p>A sessão técnica tem piloto e checkpoint próprios; não substitui nem altera sua carreira.</p>
                <button type="button" className="career-button career-button-outline" onClick={onOpenDemo}
                  data-testid="button-open-demo">
                  <ChevronRight size={16} /> Abrir demonstração
                </button>
              </article>
            </section>

            {error && <p className="career-message is-error" role="alert">{error}</p>}
            {notice && <p className="career-message is-success" role="status">{notice}</p>}
            <div className={`career-storage-banner is-${storageState}`} role="status" aria-live="polite"
              data-testid="career-storage-status">
              <span>{storageState === 'ready' || storageState === 'recovered'
                ? <CheckCircle2 size={17} /> : storageState === 'checking'
                  ? <LoaderCircle className="career-spin" size={17} /> : <ShieldCheck size={17} />}</span>
              <div><strong>{storageState === 'ready' ? 'Carreira local confirmada'
                : storageState === 'recovered' ? 'Cópia de recuperação encontrada'
                  : storageState === 'error' ? 'Save preservado para análise'
                    : storageState === 'checking' ? 'Verificando armazenamento'
                      : 'Armazenamento de carreira disponível'}</strong>
                <p>{storageMessage}</p></div>
              {storageState === 'error' && rawRecovery.current && (
                <button type="button" className="career-button career-button-outline"
                  onClick={() => downloadRawCareer(rawRecovery.current!)}>Exportar cópia</button>
              )}
            </div>
            <p className="career-local-note"><BadgeInfo size={14} /> Ponte C# local de desenvolvimento · save no navegador · não é suporte WebAssembly/offline de produção.</p>
          </>
        )}

        {screen === 'new' && (
          <section className="career-form-layout">
            <div className="career-form-intro">
              <span className="career-eyebrow"><i /> NOVA CARREIRA · KART</span>
              <h1>Apresente-se<br /><em>à equipe.</em></h1>
              <p>Os campos e a elegibilidade serão confirmados pelo núcleo C#. A idade inicial vem da regra oficial do motor.</p>
              <div className="career-rule-note"><ShieldCheck size={17} /><span>Criação não altera a demonstração nem resultados anteriores.</span></div>
            </div>
            <form className="career-form" onSubmit={event => { event.preventDefault(); createCareer(); }}>
              <div className="career-form-head"><span className="career-card-kicker">DADOS DO PILOTO</span><span>01 / 02</span></div>
              <label>Nome do piloto
                <input required minLength={2} maxLength={60} value={name} onChange={event => setName(event.target.value)}
                  placeholder="Ex.: Marina Costa" autoComplete="name" data-testid="input-career-name" />
              </label>
              <label>Nacionalidade
                <input required minLength={2} maxLength={60} value={nationality}
                  onChange={event => setNationality(event.target.value)}
                  placeholder="Ex.: Brasil" data-testid="input-career-nationality" />
              </label>
              <label>Origem
                <select required value={origin} onChange={event => setOrigin(event.target.value as CareerOrigin)}
                  disabled={!catalog} data-testid="select-career-origin">
                  <option value="" disabled>Selecione uma origem</option>
                  {origins.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
                </select>
              </label>
              <div className="career-form-divider"><span>02 / 02</span><span>EQUIPE INICIAL · CATÁLOGO C#</span></div>
              <label>Equipe elegível
                <select required value={teamId} onChange={event => setTeamId(event.target.value)}
                  disabled={!catalog} data-testid="select-career-team">
                  {catalog?.teams.map(team => <option key={team.id} value={team.id}>{team.name}</option>)}
                </select>
              </label>
              <label>Seed determinística
                <input required type="number" min="0" max="4294967295" step="1" value={seedText}
                  onChange={event => setSeedText(event.target.value)} data-testid="input-career-seed" />
                <small>A mesma seed e as mesmas decisões permitem reproduzir a simulação.</small>
              </label>
              {error && <p className="career-message is-error" role="alert">{error}</p>}
              <div className="career-form-actions">
                <button type="button" className="career-button career-button-outline" onClick={() => setScreen('home')} disabled={busy}>Cancelar</button>
                <button type="submit" className="career-button career-button-gold" disabled={busy || !catalog || !origin || !teamId}>
                  {busy ? <LoaderCircle className="career-spin" size={16} /> : <CheckCircle2 size={16} />}
                  {busy ? 'Confirmando no C#…' : 'Criar piloto e carreira'}
                </button>
              </div>
              <p className="career-form-foot"><ShieldCheck size={13} /> A carreira só aparece após confirmação do C# e do IndexedDB.</p>
            </form>
          </section>
        )}

        {screen === 'career' && career && checkpoint && (
          <section className="career-dashboard" data-testid="career-dashboard">
            <div className="career-dashboard-heading">
              <div>
                <span className="career-eyebrow"><i /> CENTRAL DE CARREIRA · {career.progression.currentCategoryId === 'f4' ? 'FÓRMULA 4' : 'KART'}</span>
                <h1>{career.progression.currentCategoryId === 'f4'
                  ? <>Nova etapa <em>da carreira.</em></>
                  : career.finished
                    ? <>Temporada <em>encerrada.</em></>
                    : <>Temporada <em>em andamento.</em></>}</h1>
              </div>
              <span className="career-engine-tag"><span />
                {career.progression.currentCategoryId === 'f4'
                  ? 'PROMOÇÃO CONFIRMADA PELO C#'
                  : career.finished ? 'CAMPEONATO ENCERRADO PELO C#' : 'ESTADO CONFIRMADO PELO C#'}
              </span>
            </div>
            <div className="career-profile-card">
              <div className="career-profile-monogram">{career.profile.name.trim().charAt(0).toLocaleUpperCase('pt-BR')}</div>
              <div className="career-profile-copy">
                <span className="career-card-kicker">PILOTO · {career.profile.age} ANOS</span>
                <h2>{career.profile.name}</h2>
                <p>{career.profile.nationality} <b>·</b> {originLabels[career.profile.origin]}</p>
              </div>
              <div className="career-profile-team"><span>EQUIPE ATUAL</span><strong>{career.currentTeamName}</strong><small>{career.currentTeamId}</small></div>
              <div className="career-profile-season"><span>PROGRESSO</span><strong>{career.completedRounds}<small> / {career.totalRounds}</small></strong><small>etapas concluídas</small></div>
            </div>

            <div className="career-dashboard-grid">
              <section className="career-calendar-panel">
                <div className="career-panel-head"><div><span className="career-card-kicker">HISTÓRICO KART · TEMPORADA 01</span><h2>Calendário oficial</h2></div><span>08 ETAPAS</span></div>
                <ol className="career-calendar-list" data-testid="career-calendar">
                  {career.calendar.map(round => (
                    <li key={round.number} className={`${round.number === career.nextRound && !career.finished ? 'is-next' : ''} ${round.status === 'Concluida' ? 'is-done' : ''}`}
                      data-round={round.number} data-status={round.status}>
                      <span className="career-round-number">{String(round.number).padStart(2, '0')}</span>
                      <span className="career-round-main"><strong>{round.circuitName}</strong><small>{round.status === 'Concluida' ? `Etapa ${round.number} · resultado oficial` : `Etapa ${round.number}`}</small></span>
                      <span className="career-round-status">{round.number === career.nextRound && !career.finished
                        ? 'PRÓXIMA' : statusLabels[round.status] ?? round.status}</span>
                    </li>
                  ))}
                </ol>
              </section>
              <aside className="career-season-panel">
                <div className="career-panel-head"><div><span className="career-card-kicker">RESUMO</span><h2>Estado da temporada</h2></div><CircleDashed size={18} /></div>
                <dl className="career-season-stats">
                  <div><dt>Categoria atual</dt><dd>{career.progression.currentCategoryId === 'f4' ? 'Fórmula 4' : 'Kart'}</dd></div>
                  <div><dt>Seed</dt><dd>{career.seed}</dd></div>
                  <div><dt>Próxima etapa</dt><dd>{career.finished ? 'Temporada encerrada' : `${career.nextRound} / ${career.totalRounds}`}</dd></div>
                  <div><dt>Treinamento</dt><dd>{career.credits} créditos disponíveis</dd></div>
                </dl>
                {career.completedRounds === 0 ? (
                  <div className="career-empty-results" data-testid="career-empty-results">
                    <BadgeInfo size={15} />
                    <span>Nenhuma corrida foi disputada nesta carreira. Classificação e resultados aparecem após confirmação do motor.</span>
                  </div>
                ) : (
                  <div className="career-results-summary" data-testid="career-results-summary">
                    {career.finished ? (
                      <div data-testid="career-champion">
                        <span className="career-card-kicker">CAMPEÃO OFICIAL · KART</span>
                        {career.driverStandings[0] ? (
                          <>
                            <strong>{career.driverStandings[0].name}</strong>
                            <p>{career.driverStandings[0].points} pontos · {career.driverStandings[0].wins} vitórias</p>
                          </>
                        ) : <p>O motor C# não retornou uma classificação final.</p>}
                        {career.driverStandings.find(row => row.isProtagonist) && (
                          <p data-testid="career-final-player-result">
                            Seu resultado: {career.driverStandings.find(row => row.isProtagonist)!.position}º lugar · {career.driverStandings.find(row => row.isProtagonist)!.points} pontos
                          </p>
                        )}
                        <small>{career.teamStandings[0]
                          ? `Equipe campeã: ${career.teamStandings[0].name} · ${career.teamStandings[0].points} pontos`
                          : 'Classificação final de equipes indisponível.'}</small>
                      </div>
                    ) : (
                      <>
                        <span className="career-card-kicker">ÚLTIMA ETAPA CONCLUÍDA</span>
                        {career.calendar.filter(round => round.status === 'Concluida').slice(-1).map(round => (
                          <div key={round.number}>
                            <strong>{round.circuitName}</strong>
                            {round.classification.find(result => result.isProtagonist)
                              ? <p>{round.classification.find(result => result.isProtagonist)!.position}º lugar · {round.classification.find(result => result.isProtagonist)!.points} pts</p>
                              : <p>Resultado do protagonista indisponível</p>}
                          </div>
                        ))}
                      </>
                    )}
                  </div>
                )}
                <button type="button" className="career-button career-button-gold career-save-button"
                  onClick={saveAndContinue} disabled={busy || pendingRaceCommit !== null} data-testid="button-save-career">
                  {busy ? <LoaderCircle className="career-spin" size={16} /> : <Save size={16} />}
                  {busy ? 'Validando e salvando…' : 'Salvar e continuar'}
                </button>
                <p className="career-save-caption"><ShieldCheck size={13} /> O checkpoint será validado pelo C# antes da confirmação local.</p>
              </aside>
            </div>

            {career.progression.offerAvailable && (
              <section className="career-history-panel career-progression-panel" data-testid="promotion-offer">
                <div className="career-panel-head">
                  <div><span className="career-card-kicker">OPORTUNIDADE DE CARREIRA</span><h2>Oferta para a Fórmula 4</h2></div>
                  <span>DECISÃO DO PILOTO</span>
                </div>
                <p>{career.progression.eligibilityRule}</p>
                <label className="career-promotion-team">
                  Equipe F4
                  <select value={promotionTeamId} onChange={event => setPromotionTeamId(event.target.value)} disabled={busy}>
                    {career.progression.offeredTeams.map(team => (
                      <option key={team.id} value={team.id}>{team.name}</option>
                    ))}
                  </select>
                </label>
                <div className="career-form-actions">
                  <button type="button" className="career-button career-button-outline"
                    onClick={declinePromotion} disabled={busy} data-testid="button-decline-promotion">
                    Recusar oferta
                  </button>
                  <button type="button" className="career-button career-button-gold"
                    onClick={acceptPromotion} disabled={busy || !promotionTeamId}
                    data-testid="button-accept-promotion">
                    {busy ? 'Confirmando no C# e salvando…' : 'Aceitar promoção'}
                  </button>
                </div>
                <small>A decisão só aparece como concluída depois da confirmação C# e IndexedDB.</small>
              </section>
            )}

            {career.progression.currentCategoryId === 'f4' && (
              <section className="career-history-panel career-progression-panel" data-testid="f4-season">
                <div className="career-panel-head">
                  <div><span className="career-card-kicker">MOTOR F4 NO NÚCLEO · WEB PENDENTE</span><h2>Calendário da Fórmula 4</h2></div>
                  <span>10 ETAPAS</span>
                </div>
                <p>O motor esportivo F4 existe no núcleo C# e na ponte versionada. A central Web ainda não oferece comandos de corrida até concluir a validação de integração.</p>
                {career.progression.seasons.filter(season => season.categoryId === 'f4').map(season => (
                  <div key={`${season.categoryId}-${season.seasonNumber}`}>
                    <p>{season.teamName} · {season.completedRounds}/{season.totalRounds} etapas concluídas</p>
                    <ol className="career-calendar-list">
                      {season.calendar.map(round => (
                        <li key={round.id} data-testid={`f4-round-${round.number}`}>
                          <span className="career-round-number">{String(round.number).padStart(2, '0')}</span>
                          <span className="career-round-main"><strong>{round.name}</strong><small>Etapa configurada · controles Web pendentes</small></span>
                          <span className="career-round-status">PENDENTE</span>
                        </li>
                      ))}
                    </ol>
                    <div className="career-f4-roster">
                      <span className="career-card-kicker">PARTICIPANTES · DADOS FICTÍCIOS</span>
                      <ul>
                        {season.participants.map(participant => (
                          <li key={participant.id} className={participant.isPlayer ? 'is-player' : ''}>
                            <span>{participant.name}{participant.isPlayer ? ' · VOCÊ' : ''}</span>
                            <small>{career.progression.formula4Teams.find(team => team.id === participant.teamId)?.name ?? participant.teamId}</small>
                          </li>
                        ))}
                      </ul>
                    </div>
                  </div>
                ))}
              </section>
            )}

            <section className="career-history-panel career-progression-panel" data-testid="career-category-path">
              <div className="career-panel-head">
                <div><span className="career-card-kicker">CARREIRA MULTICATEGORIA</span><h2>Caminho esportivo</h2></div>
                <span>{career.progression.currentCategoryId.toUpperCase()}</span>
              </div>
              <ol className="career-category-list">
                {career.progression.categories.map(category => {
                  const hasSeason = career.progression.seasons.some(season => season.categoryId === category.id);
                  const isCurrent = career.progression.currentCategoryId === category.id;
                  const status = category.implemented
                    ? category.id === 'f4'
                      ? 'Motor C# disponível; integração Web pendente'
                      : 'Motor Kart disponível'
                    : hasSeason
                      ? 'Calendário estrutural; motor pendente'
                      : category.id === 'f4' && career.progression.offerAvailable
                        ? 'Oferta disponível; corridas pendentes'
                        : 'Apenas configuração';
                  return (
                    <li key={category.id} className={isCurrent ? 'is-current' : ''}>
                      <span className="career-round-number">{String(category.rounds).padStart(2, '0')}</span>
                      <span><strong>{category.name}</strong><small>{status}</small></span>
                      {isCurrent && <b>ATUAL</b>}
                    </li>
                  );
                })}
              </ol>
              <div className="career-team-history">
                <span className="career-card-kicker">HISTÓRICO DE EQUIPES · C#</span>
                <ul>
                  {career.progression.teamHistory.map(entry => (
                    <li key={`${entry.categoryId}-${entry.seasonNumber}`}>
                      <span>{entry.categoryId.toUpperCase()} · Temporada {entry.seasonNumber}</span>
                      <strong>{entry.teamName}</strong>
                    </li>
                  ))}
                </ul>
              </div>
            </section>

            {!career.finished && (
              <section className="career-history-panel career-preparation-panel" data-testid="career-preparation">
                <div className="career-panel-head">
                  <div>
                    <span className="career-card-kicker">PREPARAÇÃO ENTRE ETAPAS</span>
                    <h2>Preparação para a próxima corrida</h2>
                  </div>
                  <span>ETAPA {String(career.nextRound).padStart(2, '0')}</span>
                </div>
                <p className="career-preparation-intro">
                  {career.profile.name} · {career.profile.teamName} · {career.calendar[career.nextRound - 1]?.circuitName}
                </p>
                {career.currentPreparation ? (
                  <div className="career-preparation-confirmed" data-testid="career-preparation-result" role="status">
                    <strong>Preparação oficial confirmada pelo C#</strong>
                    <p>
                      {trainingLabels[career.currentPreparation.training]} · Ritmo inicial: {career.currentPreparation.pace}
                    </p>
                    <small>
                      Habilidade aplicada: {career.currentPreparation.skillApplied} ·
                      Créditos restantes: {career.currentPreparation.creditsRemaining}
                    </small>
                    <small>Esta etapa já está preparada; outra ação não pode ser aplicada.</small>
                  </div>
                ) : (
                  <>
                    <div className="career-preparation-fields">
                      <label>
                        Ação de preparação
                        <select
                          value={training}
                          onChange={event => setTraining(event.target.value as CareerTraining)}
                          disabled={busy || career.prepared}
                          data-testid="select-career-training"
                        >
                          {career.preparationOptions.map(option => (
                            <option key={option.training} value={option.training} disabled={!option.available}>
                              {trainingLabels[option.training]} · {option.creditCost} créditos
                            </option>
                          ))}
                        </select>
                      </label>
                      <label>
                        Ritmo de largada
                        <select
                          value={pace}
                          onChange={event => setPace(event.target.value as CareerPace)}
                          disabled={busy || career.prepared}
                          data-testid="select-career-pace"
                        >
                          {career.paceOptions.map(option => <option key={option} value={option}>{option}</option>)}
                        </select>
                      </label>
                    </div>
                    <div className="career-preparation-cost" data-testid="career-preparation-cost">
                      <span>
                        Custo informado pelo C#: {selectedTraining?.creditCost ?? '—'} crédito(s)
                      </span>
                      <span>
                        Consequência: {selectedTraining?.skillDelta === undefined
                          ? 'não disponível'
                          : selectedTraining.skillDelta > 0
                            ? `+${selectedTraining.skillDelta} de habilidade`
                            : 'sem alteração de habilidade'}
                      </span>
                      {selectedTraining?.unavailableReason && (
                        <small role="alert">{selectedTraining.unavailableReason}</small>
                      )}
                    </div>
                    <button
                      type="button"
                      className="career-button career-button-gold career-preparation-confirm"
                      onClick={prepareCareer}
                      disabled={busy || pendingRaceCommit !== null || !selectedTraining?.available || career.prepared}
                      data-testid="button-confirm-preparation"
                    >
                      {busy ? <LoaderCircle className="career-spin" size={16} /> : <CheckCircle2 size={16} />}
                      {busy ? 'Confirmando no C# e salvando…' : 'Confirmar preparação'}
                    </button>
                  </>
                )}
              </section>
            )}

            {pendingRaceCommit && (
              <section className="career-pending-race-save" role="alert" data-testid="career-pending-race-save">
                <strong>Checkpoint C# sem confirmação local</strong>
                <p>
                  Não envie outro comando de corrida. Tente gravar exatamente a resposta já recebida,
                  ou recarregue a carreira para verificar a cópia persistida.
                </p>
                {error && <p className="career-message is-error">{error}</p>}
                <button type="button" className="career-button career-button-outline"
                  onClick={retryPendingRaceSave} disabled={busy}
                  data-testid="button-retry-career-save">
                  {busy ? 'Verificando e salvando…' : 'Tentar salvar o mesmo checkpoint'}
                </button>
              </section>
            )}

            {!career.finished && (
              <section className="career-race-launch" aria-label="Acesso à próxima corrida da carreira"
                data-testid="career-race-launch">
                <div>
                  <span className="career-card-kicker">CORRIDA OFICIAL · KART</span>
                  <h2 data-testid="career-next-round">{activeCareerRace
                    ? `Etapa ${String(activeCareerRace.round).padStart(2, '0')} em andamento`
                    : `Etapa ${String(career.nextRound).padStart(2, '0')} · ${career.calendar.find(round => round.number === career.nextRound)?.circuitName ?? 'Circuito não disponível'}`}
                  </h2>
                  <p>
                    {activeCareerRace
                      ? `${activeCareerRace.completedSectors}/${activeCareerRace.progress.totalSectors} setores salvos. Retomar não avança a corrida.`
                      : 'A etapa começa somente após preparar a carreira. O elenco, setores, resultado e pontos vêm do motor C#.'}
                  </p>
                </div>
                {activeCareerRace ? (
                  <button type="button" className="career-button career-button-gold"
                    onClick={() => setScreen('race')} disabled={busy || pendingRaceCommit !== null}
                    data-testid="button-resume-career-race">
                    <ChevronRight size={16} /> Retomar corrida
                  </button>
                ) : (
                  <button type="button" className="career-button career-button-gold"
                    onClick={startCareerRace} disabled={busy || !career.prepared || pendingRaceCommit !== null}
                    data-testid="button-start-career-race">
                    {busy ? <LoaderCircle className="career-spin" size={16} /> : <ChevronRight size={16} />}
                    {busy ? 'Iniciando no C#…' : 'Ir para a corrida'}
                  </button>
                )}
              </section>
            )}

            {career.completedRounds > 0 && (
              <section className="career-standings-panel">
                <div className="career-panel-head"><div><span className="career-card-kicker">CLASSIFICAÇÃO OFICIAL</span><h2>Pilotos</h2></div><span>{career.completedRounds} ETAPAS</span></div>
                <div className="career-standings-table-wrap"><table className="career-standings-table">
                  <thead><tr><th>Pos.</th><th>Piloto</th><th>Pontos</th><th>Vitórias</th></tr></thead>
                  <tbody>{career.driverStandings.map(row => (
                    <tr key={row.id} className={row.isProtagonist ? 'is-player' : ''}>
                      <td>{row.position}º</td><td>{row.name}{row.isProtagonist && <small>VOCÊ</small>}</td><td>{row.points}</td><td>{row.wins}</td>
                    </tr>
                  ))}</tbody>
                </table></div>
                <div className="career-panel-head career-team-standings-heading"><div><span className="career-card-kicker">EQUIPES</span><h2>Classificação</h2></div></div>
                <div className="career-standings-table-wrap"><table className="career-standings-table">
                  <thead><tr><th>Pos.</th><th>Equipe</th><th>Pontos</th><th>Vitórias</th></tr></thead>
                  <tbody>{career.teamStandings.map(row => (
                    <tr key={row.id} className={row.isProtagonist ? 'is-player' : ''}>
                      <td>{row.position}º</td><td>{row.name}{row.isProtagonist && <small>SUA EQUIPE</small>}</td><td>{row.points}</td><td>{row.wins}</td>
                    </tr>
                  ))}</tbody>
                </table></div>
              </section>
            )}
            {career.completedRounds > 0 && (
              <section className="career-history-panel" data-testid="career-history">
                <div className="career-panel-head"><div><span className="career-card-kicker">REGISTROS CONFIRMADOS</span><h2>Histórico da temporada</h2></div></div>
                <ol className="career-history-list">
                  {career.calendar.filter(round => round.status === 'Concluida').map(round => {
                    const result = round.classification.find(row => row.isProtagonist);
                    return (
                      <li key={round.number}>
                        <span className="career-history-round">ETAPA {String(round.number).padStart(2, '0')}</span>
                        <div><strong>{round.circuitName}</strong>
                          <p>{result ? `${result.position}º lugar · ${result.points} pontos · ${(result.elapsedMs / 1000).toFixed(3).replace('.', ',')} s` : 'Resultado do protagonista indisponível'}
                          </p>
                          {round.highlights.length > 0 && <small>{round.highlights.slice(0, 2).join(' · ')}</small>}
                        </div>
                      </li>
                    );
                  })}
                </ol>
              </section>
            )}
            {error && <p className="career-message is-error" role="alert">{error}</p>}
            {notice && <p className="career-message is-success" role="status">{notice}</p>}
            <div className={`career-storage-banner is-${storageState}`} role="status" aria-live="polite">
              <span><CheckCircle2 size={17} /></span><div><strong>{storageState === 'recovered' ? 'Cópia recuperada e validada' : 'Carreira local confirmada'}</strong><p>{storageMessage}</p></div>
            </div>
            <div className="career-next-note"><BadgeInfo size={15} /><span>
              {career.progression.currentCategoryId === 'f4'
                  ? 'Promoção e temporada F4 confirmadas pelo C#; as oito etapas Kart permanecem no histórico. A corrida F4 já tem motor no núcleo; a central Web aguarda integração validada.'
                : career.progression.decision === 'Declined'
                  ? 'A oferta F4 foi recusada e registrada. A carreira permanece na temporada Kart concluída, sem repetir provas.'
                  : career.finished
                    ? 'Temporada Kart encerrada pelo C#; classificação, pontuação e histórico permanecem no checkpoint. Há uma oferta de progressão disponível.'
                : career.completedRounds > 0
                  ? `Etapa ${career.completedRounds} confirmada pelo C#; etapa ${career.nextRound} liberada após preparação.`
                  : 'A demonstração técnica permanece separada; esta carreira usa a sessão C# e o checkpoint próprios.'}
            </span></div>
          </section>
        )}

        {screen === 'new' && error && <p className="career-message is-error" role="alert">{error}</p>}
        {screen === 'new' && notice && <p className="career-message is-success" role="status">{notice}</p>}
      </div>
      <footer className="career-footer"><span>STINTORIA · MOTORSPORT PRESTIGE</span><span>Offline local · motor C# autoritativo</span></footer>
    </main>
  );
}
