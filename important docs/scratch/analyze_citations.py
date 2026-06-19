import os
import re

def extract_citations_from_file(filepath):
    with open(filepath, "r", encoding="utf-8") as f:
        content = f.read()
    
    # Simple regex to find \cite{key1} or \cite{key1, key2}
    # Matches \cite{...}
    citations = []
    # Avoid comments
    clean_lines = []
    for line in content.split("\n"):
        # Strip comments
        if "%" in line:
            # find first % not preceded by \
            clean_line = ""
            escaped = False
            for char in line:
                if char == "\\" and not escaped:
                    escaped = True
                    clean_line += char
                elif char == "%" and not escaped:
                    break
                else:
                    escaped = False
                    clean_line += char
            clean_lines.append(clean_line)
        else:
            clean_lines.append(line)
            
    clean_content = "\n".join(clean_lines)
    matches = re.finditer(r"\\cite\{([^{}]+)\}", clean_content)
    for m in matches:
        keys = [k.strip() for k in m.group(1).split(",")]
        citations.extend(keys)
    return citations

def main():
    tex_dir = r"c:\Users\DELL\Desktop\AI_Labmonitoring\important docs\fyp_doc_3chatp"
    
    # We want to trace order of compilation
    # Let's read FYP-Final.tex to get the exact compilation order of chapters
    final_tex = os.path.join(tex_dir, "FYP-Final.tex")
    with open(final_tex, "r", encoding="utf-8") as f:
        final_content = f.read()
        
    chapter_files = []
    # Find all \input{Chapters/...}
    input_matches = re.finditer(r"\\input\{Chapters/([^}]+)\}", final_content)
    for m in input_matches:
        filename = m.group(1)
        if not filename.endswith(".tex"):
            filename += ".tex"
        # Ignore Chapter-9-Bibliography
        if "Bibliography" not in filename:
            chapter_files.append(filename)
            
    print(f"Compilation order of files: {chapter_files}")
    
    cited_keys_in_order = []
    seen = set()
    cited_per_file = {}
    
    for filename in chapter_files:
        filepath = os.path.join(tex_dir, "Chapters", filename)
        if os.path.exists(filepath):
            file_citations = extract_citations_from_file(filepath)
            cited_per_file[filename] = file_citations
            for key in file_citations:
                if key not in seen:
                    seen.add(key)
                    cited_keys_in_order.append(key)
        else:
            print(f"Warning: file {filepath} does not exist.")
            
    # Now read bibitems in Chapter-9-Bibliography.tex
    bib_file = os.path.join(tex_dir, "Chapters", "Chapter-9-Bibliography.tex")
    bib_keys = []
    if os.path.exists(bib_file):
        with open(bib_file, "r", encoding="utf-8") as f:
            bib_content = f.read()
        bib_matches = re.finditer(r"\\bibitem\{([^}]+)\}", bib_content)
        bib_keys = [m.group(1).strip() for m in bib_matches]
    
    print("\n--- RESULTS ---")
    print(f"Total unique keys cited in text: {len(cited_keys_in_order)}")
    print(f"Total keys defined in Bibliography: {len(bib_keys)}")
    
    missing_in_bib = [k for k in cited_keys_in_order if k not in bib_keys]
    unused_in_bib = [k for k in bib_keys if k not in seen]
    
    print(f"\nKeys cited in text but MISSING from Bibliography: {missing_in_bib}")
    print(f"Keys defined in Bibliography but UNUSED in text: {unused_in_bib}")
    
    print("\nOrder of first appearance in text:")
    for idx, key in enumerate(cited_keys_in_order):
        print(f"  [{idx+1}] {key}")

if __name__ == "__main__":
    main()
