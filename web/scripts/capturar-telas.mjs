// Gera os prints do README: cria uma conta de demonstração com dados fictícios pela API e fotografa as telas.
// Uso: com a API rodando (dotnet run --project src/Indexa.Api) e o painel compilado (npm run build):
//   node scripts/capturar-telas.mjs [http://localhost:5080]
import { chromium } from 'playwright';
import { mkdir } from 'node:fs/promises';

const BASE = process.argv[2] ?? 'http://localhost:5080';
const SAIDA = new URL('../../docs/telas/', import.meta.url);
const email = `demo-${Date.now()}@indexa.exemplo`;
const senha = 'demonstracao-123';

async function chamar(metodo, caminho, corpo, token) {
  const r = await fetch(`${BASE}/api${caminho}`, {
    method: metodo,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: corpo ? JSON.stringify(corpo) : undefined,
  });
  if (!r.ok) throw new Error(`${metodo} ${caminho}: ${r.status} ${await r.text()}`);
  return r.status === 204 ? null : r.json();
}

await chamar('POST', '/auth/registrar', { nome: 'Marina Duarte', email, senha });
const { token } = await chamar('POST', '/auth/login', { email, senha });
await chamar('PUT', '/configuracoes', { antecedenciaAvisoDias: 40, emailAviso: null, avisoEmailAtivo: true }, token);

// Clientes e contratos fictícios.
const cliente = async (nome, documento) => (await chamar('POST', '/clientes', { nome, documento }, token)).id;
const horizonte = await cliente('Imobiliária Horizonte', '12.ABC.345/01DE-35');
const aurora = await cliente('Condomínio Edifício Aurora', null);
const clinica = await cliente('Clínica Bem Estar', null);
const lima = await cliente('Lima & Souza Contabilidade', null);

const contrato = (clienteId, descricao, valorInicial, dataBase, tipoIndice, extra = {}) =>
  chamar('POST', '/contratos', { clienteId, descricao, valorInicial, dataBase, tipoIndice, naoReduzir: false, ...extra }, token);

const loja = await contrato(horizonte, 'Aluguel loja 3 — Rua das Flores', 4800, '2023-03-15', 'Ipca');
await contrato(horizonte, 'Aluguel apartamento 52', 2350, '2022-07-01', 'IgpM', { piso: 0, unidadeLimite: 'Percentual' });
const elevadores = await contrato(aurora, 'Manutenção de elevadores', 3900, '2024-11-10', 'IgpM', { naoReduzir: true });
await contrato(aurora, 'Portaria terceirizada', 18500, '2025-12-01', 'Ipca');
await contrato(clinica, 'Locação sala 204', 2500, '2025-11-20', 'Ipca', { teto: 4, unidadeLimite: 'Percentual' });
await contrato(lima, 'Honorários contábeis', 1650, '2024-01-05', 'PercentualFixo', { percentualFixo: 5 });

await mkdir(SAIDA, { recursive: true });
const navegador = await chromium.launch();
const pagina = await navegador.newPage({ viewport: { width: 1366, height: 860 }, deviceScaleFactor: 1.5, locale: 'pt-BR', colorScheme: 'light' });

async function foto(caminho, arquivo, esperar) {
  await pagina.goto(`${BASE}${caminho}`);
  await pagina.waitForLoadState('networkidle');
  if (esperar) await pagina.waitForSelector(esperar);
  await pagina.screenshot({ path: new URL(arquivo, SAIDA).pathname.replace(/^\/([A-Za-z]:)/, '$1'), fullPage: true });
  console.log('ok', arquivo);
}

await foto('/login', '0-login.png', 'form');
// Mesma sessão que o login criaria: cookie HttpOnly com o token.
await pagina.context().addCookies([{ name: 'indexa_sessao', value: token, url: BASE, httpOnly: true, sameSite: 'Strict' }]);
await foto('/', '1-painel.png', '.metricas');
await foto('/contratos', '2-contratos.png', '.tabela');
await foto('/contratos/novo', '3-novo-contrato.png', '.formulario');
await foto(`/contratos/${loja.id}`, '4-detalhe-retroativos.png', '.linha-do-tempo');
await foto(`/contratos/${elevadores.id}`, '4b-detalhe-nao-reducao.png', '.linha-do-tempo');
await foto('/configuracoes', '5-configuracoes.png', '.formulario');

await pagina.setViewportSize({ width: 390, height: 844 });
await foto('/', '6-painel-celular.png', '.metricas');
await navegador.close();
