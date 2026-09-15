# Histórico de versões

Todas as mudanças relevantes deste projeto são documentadas aqui.

## 1.1.0 — 2026-09-15

- Adiciona gerador de senhas personalizável com comprimento, grupos de caracteres, mínimos de números e símbolos e exclusão de caracteres ambíguos.
- Adiciona pastas cifradas para criar, renomear, excluir, filtrar e organizar credenciais.
- Preserva as credenciais em “Sem pasta” quando uma pasta é excluída.
- Migra automaticamente cofres 1.0 existentes sem alterar ou apagar os dados do usuário.
- Inclui pastas cifradas nos backups e snapshots de sincronização, mantendo compatibilidade com backups anteriores.
- Passa a publicar um instalador por usuário (`ScanFace-Setup.exe`) junto do executável portátil.

## 1.0.2 — 2026-09-15

- Corrige o encerramento ao abrir a tela do cofre depois da autenticação.
- Define como somente leitura o binding do contador de credenciais.
- Registra erros inesperados de interface em `%LOCALAPPDATA%\ScanFace\error.log` antes de encerrar.
- Amplia o teste de regressão para validar os bindings das telas de configuração e do cofre.

## 1.0.1 — 2026-09-15

- Corrige a captura de senhas digitadas nos controles protegidos do WPF.
- Impede que o binding seja removido ao editar senha mestra, confirmação, token da API ou senha de credencial.
- Exibe uma mensagem direta quando a senha mestra realmente tiver menos de 12 caracteres.
- Adiciona um teste de regressão para o fluxo de criação do cofre.

## 1.0.0 — 2026-09-15

- Primeiro cofre local com AES-256-GCM e Argon2id.
- Desbloqueio por senha mestra ou Windows Hello sem digitar a senha.
- Aplicação WPF organizada em MVVM e Clean Architecture simplificada.
- CRUD, pesquisa, favoritos, gerador de senhas, auto bloqueio e limpeza do clipboard.
- Backup cifrado e sincronização opcional com API ASP.NET Core/Docker.
- Testes automatizados, documentação de arquitetura e modelo de ameaça.
