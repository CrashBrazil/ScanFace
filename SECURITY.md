# Política de segurança

## Versões suportadas

A versão mais recente publicada em [Releases](https://github.com/CrashBrazil/ScanFace/releases) recebe correções de segurança.

## Relatar uma vulnerabilidade

Não abra uma issue pública com dados que facilitem exploração. Use o recurso **Security > Report a vulnerability** do GitHub deste repositório e inclua versão, ambiente, impacto, passos mínimos de reprodução e evidências sem segredos reais.

## Escopo do projeto

O ScanFace cifra as credenciais antes de persistir ou sincronizar, mas um gerenciador de senhas não elimina o risco de um sistema operacional já comprometido. Consulte o [modelo de ameaça](docs/THREAT_MODEL.md) antes de avaliar ou implantar o aplicativo.
