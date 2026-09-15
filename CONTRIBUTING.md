# Como contribuir

1. Crie um fork e uma branch curta para a mudança.
2. Mantenha as dependências na direção definida em `docs/ARCHITECTURE.md`.
3. Não registre senhas, chaves, tokens, conteúdo descriptografado ou templates biométricos.
4. Execute `dotnet build ScanFace.slnx --configuration Release` e `dotnet test ScanFace.slnx --configuration Release`.
5. Explique no pull request o comportamento alterado e os testes realizados.

Mudanças criptográficas devem incluir vetores ou testes de adulteração relevantes. Mudanças de UI devem manter teclado, contraste e mensagens compreensíveis em português.
