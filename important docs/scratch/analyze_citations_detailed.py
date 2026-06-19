import os
import re

def main():
    tex_dir = r"c:\Users\DELL\Desktop\AI_Labmonitoring\important docs\fyp_doc_3chatp"
    
    # Read FYP-Final.tex to get the exact compilation order of chapters
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
        if "Bibliography" not in filename:
            chapter_files.append(filename)
            
    print(f"Compilation order of files: {chapter_files}\n")
    
    all_citations = []
    # Trace each citation as (filename, line_no, list_of_keys, raw_match)
    for filename in chapter_files:
        filepath = os.path.join(tex_dir, "Chapters", filename)
        if not os.path.exists(filepath):
            continue
        with open(filepath, "r", encoding="utf-8") as f:
            lines = f.readlines()
        for idx, line in enumerate(lines):
            line_no = idx + 1
            # Strip comments
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
            
            matches = re.finditer(r"\\cite\{([^{}]+)\}", clean_line)
            for m in matches:
                keys = [k.strip() for k in m.group(1).split(",")]
                all_citations.append({
                    "file": filename,
                    "line": line_no,
                    "keys": keys,
                    "raw": m.group(0)
                })

    print(f"Total citations found: {len(all_citations)}")
    seen_keys = []
    seen_set = set()
    
    print("\nCitation instances in text:")
    for idx, c in enumerate(all_citations):
        print(f"{idx+1}. {c['file']}:{c['line']} - {c['raw']}")
        for k in c['keys']:
            if k not in seen_set:
                seen_set.add(k)
                seen_keys.append(k)
                
    # Now check if there are citations of older keys after newer keys have been introduced
    # In a perfect sequential order, the first time we see key index in seen_keys, it increases.
    # What if a key is cited again?
    print("\nKey citation occurrences:")
    key_occ = {}
    for idx, c in enumerate(all_citations):
        for k in c['keys']:
            key_occ.setdefault(k, []).append(f"{c['file']}:{c['line']}")
            
    for k, occurrences in key_occ.items():
        if len(occurrences) > 1:
            print(f"  Key '{k}' cited multiple times: {occurrences}")
            
if __name__ == "__main__":
    main()
