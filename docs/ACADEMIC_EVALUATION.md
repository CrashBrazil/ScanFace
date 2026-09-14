# Roteiro de avaliação acadêmica

O projeto pode ser apresentado como “gerenciador de senhas offline first, zero knowledge, com autenticação facial processada localmente e sincronização opcional”.

## Hipóteses demonstráveis

1. Uma cópia do banco local não revela campos das credenciais.
2. Uma cópia do banco da API não revela campos das credenciais.
3. Modificar um byte do ciphertext ou da tag invalida o registro.
4. Uma senha mestra incorreta não abre a chave do cofre.
5. Depois do cadastro local, o Windows Hello abre o cofre sem digitar a senha mestra.
6. O envelope biométrico de uma máquina não aparece no backup nem na sincronização.
7. Duas atualizações concorrentes não se sobrescrevem silenciosamente.

## Experimentos sugeridos

- Criar uma credencial com marcadores únicos e procurar esses textos em `vault.db`, no `.scanface` e no SQLite da API.
- Alterar manualmente ciphertext, nonce e tag em cópias do banco e registrar a rejeição do AES-GCM.
- Medir tempo e memória da derivação Argon2id no computador de teste.
- Comparar desbloqueio por senha e por Windows Hello.
- Testar Windows Hello com rosto válido, rosto inválido, fotografia, vídeo e alternativas oferecidas pelo Windows; registrar que a detecção de vivacidade é responsabilidade do hardware e do sistema.
- Desconectar a rede, criar e editar itens, e confirmar que todas as funções locais continuam disponíveis.
- Produzir um conflito remoto e verificar que nenhum lado é sobrescrito.

## Métricas

- tempo médio e percentis de desbloqueio;
- uso máximo de memória durante Argon2id;
- taxa de sucesso e rejeição do Windows Hello em cada cenário;
- quantidade de ocorrências de texto puro encontradas nos artefatos copiados;
- tempo para CRUD com 10, 100 e 1.000 entradas;
- comportamento após corrupção, perda de rede e conflito.

Não atribua ao ScanFace uma detecção facial própria. A aplicação delega aquisição biométrica, liveness e política de fallback ao Windows Hello; esse limite deve aparecer nos resultados.
