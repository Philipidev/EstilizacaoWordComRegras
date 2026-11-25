"""Gera uma cópia leve de um .docx para usar como documento de teste no repositório.

As imagens (word/media) e os objetos incorporados (word/embeddings) respondem por mais de
95% do tamanho dos documentos reais, e o Revisor não lê o conteúdo deles: conta desenhos e
lê o texto, a estrutura, os estilos e as propriedades. Cada um desses arquivos é trocado
por um conteúdo mínimo com o mesmo nome; as referências e a contagem de imagens ficam
intactas, e docProps/app.xml (contagem de páginas do Word) não muda.

Uso: python scripts/enxugar-docx.py ORIGEM.docx DESTINO.docx
"""
import sys
import zipfile

PNG_1X1 = bytes.fromhex(
    "89504e470d0a1a0a0000000d4948445200000001000000010806000000"
    "1f15c4890000000d49444154789c6360000002000001e221bc330000000049454e44ae426082")
JPEG_1X1 = bytes.fromhex(
    "ffd8ffe000104a46494600010100000100010000ffdb004300080606070605080707070909080a0c"
    "140d0c0b0b0c1912130f141d1a1f1e1d1a1c1c20242e2720222c231c1c2837292c30313434341f27"
    "393d38323c2e333432ffc0000b080001000101011100ffc4001f0000010501010101010100000000"
    "000000000102030405060708090a0bffc400b5100002010303020403050504040000017d01020300"
    "041105122131410613516107227114328191a1082342b1c11552d1f02433627282090a161718191a"
    "25262728292a3435363738393a434445464748494a535455565758595a636465666768696a737475"
    "767778797a838485868788898a92939495969798999aa2a3a4a5a6a7a8a9aab2b3b4b5b6b7b8b9ba"
    "c2c3c4c5c6c7c8c9cad2d3d4d5d6d7d8d9dae1e2e3e4e5e6e7e8e9eaf1f2f3f4f5f6f7f8f9faffda"
    "0008010100003f00fbd3ffd9")


def substituto(nome: str) -> bytes:
    ext = nome.rsplit(".", 1)[-1].lower()
    if ext == "png":
        return PNG_1X1
    if ext in ("jpg", "jpeg"):
        return JPEG_1X1
    return b"\0"


def enxugar(origem: str, destino: str) -> None:
    with zipfile.ZipFile(origem) as entrada, \
            zipfile.ZipFile(destino, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as saida:
        for item in entrada.infolist():
            dados = entrada.read(item.filename)
            if item.filename.startswith(("word/media/", "word/embeddings/")):
                dados = substituto(item.filename)
            saida.writestr(item.filename, dados)


if __name__ == "__main__":
    enxugar(sys.argv[1], sys.argv[2])
