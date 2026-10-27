import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { sessaoAtual } from './api';
import { Layout } from './paginas/Layout';
import { Login } from './paginas/Login';
import { Painel } from './paginas/Painel';
import { Contratos } from './paginas/Contratos';
import { ContratoFormulario } from './paginas/ContratoFormulario';
import { ContratoDetalhe } from './paginas/ContratoDetalhe';
import { Configuracoes } from './paginas/Configuracoes';
import './estilos.css';

function Protegido() {
  return sessaoAtual() ? <Layout /> : <Navigate to="/login" replace />;
}

createRoot(document.getElementById('raiz')!).render(
  <StrictMode>
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route element={<Protegido />}>
          <Route index element={<Painel />} />
          <Route path="contratos" element={<Contratos />} />
          <Route path="contratos/novo" element={<ContratoFormulario />} />
          <Route path="contratos/:id" element={<ContratoDetalhe />} />
          <Route path="contratos/:id/editar" element={<ContratoFormulario />} />
          <Route path="configuracoes" element={<Configuracoes />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Route>
      </Routes>
    </BrowserRouter>
  </StrictMode>,
);
