import os
import re

def check_file(filepath):
    with open(filepath, "r", encoding="utf-8") as f:
        lines = f.readlines()
    
    issues = []
    in_verbatim = False
    
    # Matches a backslash followed by an underscore or other characters, or a comment line.
    # We want to find unescaped underscores outside of comments, labels, refs, texttt, cites, includegraphics, inputs, math mode etc.
    for idx, line in enumerate(lines):
        line_num = idx + 1
        stripped = line.strip()
        
        # Simple comments check
        if stripped.startswith("%"):
            continue
            
        # Ignore lines inside verbatim if any (unlikely in this project but good practice)
        if "\\begin{verbatim}" in line:
            in_verbatim = True
            continue
        if "\\end{verbatim}" in line:
            in_verbatim = False
            continue
        if in_verbatim:
            continue
            
        # Remove comments at the end of the line
        # but avoid removing \%
        clean_line = ""
        escaped = False
        for char_idx, char in enumerate(line):
            if char == "\\" and not escaped:
                escaped = True
                clean_line += char
            elif char == "%" and not escaped:
                # Comment starts, ignore rest of line
                break
            else:
                escaped = False
                clean_line += char

        # Find all underscores in the line
        for m in re.finditer(r"_", clean_line):
            pos = m.start()
            # Check if it is escaped (preceded by a backslash)
            # Count backslashes before the underscore
            backslash_count = 0
            curr = pos - 1
            while curr >= 0 and clean_line[curr] == "\\":
                backslash_count += 1
                curr -= 1
            
            if backslash_count % 2 == 0:
                # The underscore is unescaped!
                # Now check if it's within a safe LaTeX environment or command where unescaped underscores are allowed,
                # e.g., \label{...}, \ref{...}, \includegraphics{...}, \input{...}, \texttt{...}, \cite{...}, \begin{...}, \end{...} or math mode $...$
                
                # Check if it is inside standard commands
                # We can check if the line contains a command before it
                is_safe = False
                
                # Let's inspect the context around it.
                # A simple way: check if it's inside braces preceded by label, ref, input, include, texttt, cite
                # Let's look backwards for the command name
                prefix = clean_line[:pos]
                
                # Check for math mode (odd number of $ before and after)
                dollars_before = prefix.count("$")
                dollars_after = clean_line[pos:].count("$")
                if dollars_before % 2 == 1 and dollars_after % 2 == 1:
                    is_safe = True
                
                # Check if it is inside braces of a safe command
                # Regex to check if the last open brace before the underscore belongs to a safe command
                # e.g., \label{..._...}
                brace_match = list(re.finditer(r"\\(label|ref|texttt|cite|input|includegraphics|begin|end|chapter|section|subsection|subsubsection|bibliographystyle|bibliography)\{[^{}]*$", prefix))
                if brace_match:
                    is_safe = True
                
                # Check if it is part of path in \includegraphics
                if "includegraphics" in prefix:
                    is_safe = True
                    
                if not is_safe:
                    # Report issue
                    context = clean_line.strip()
                    issues.append((line_num, pos, context))
                    
    return issues

def main():
    tex_dir = r"c:\Users\DELL\Desktop\AI_Labmonitoring\important docs\fyp_doc_3chatp"
    all_issues = {}
    
    # Check FYP-Final.tex
    final_tex = os.path.join(tex_dir, "FYP-Final.tex")
    if os.path.exists(final_tex):
        issues = check_file(final_tex)
        if issues:
            all_issues["FYP-Final.tex"] = issues
            
    # Check Chapters directory
    chapters_dir = os.path.join(tex_dir, "Chapters")
    for root, dirs, files in os.walk(chapters_dir):
        for file in files:
            if file.endswith(".tex"):
                filepath = os.path.join(root, file)
                issues = check_file(filepath)
                if issues:
                    all_issues[os.path.join("Chapters", file)] = issues
                    
    if all_issues:
        print("FOUND POTENTIAL UNESCAPED UNDERSCORES:")
        for file, issues in all_issues.items():
            print(f"\nFile: {file}")
            for line_num, pos, context in issues:
                print(f"  Line {line_num}: col {pos} -> {context}")
    else:
        print("No unescaped underscores found!")

if __name__ == "__main__":
    main()
