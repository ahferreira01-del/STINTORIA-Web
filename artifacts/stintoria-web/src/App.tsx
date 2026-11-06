import { useState } from 'react';
import { StintoriaStage } from '@/components/stintoria/StintoriaStage';
import { CareerExperience } from '@/components/stintoria/CareerExperience';

function App() {
  const [showDemo, setShowDemo] = useState(false);
  return showDemo
    ? <StintoriaStage onBack={() => setShowDemo(false)} />
    : <CareerExperience onOpenDemo={() => setShowDemo(true)} />;
}

export default App;
