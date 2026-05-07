import { useMemo, type Ref } from 'react';
import CodeMirror, { type ReactCodeMirrorRef } from '@uiw/react-codemirror';
import { json } from '@codemirror/lang-json';
import { yaml } from '@codemirror/lang-yaml';
import { StreamLanguage } from '@codemirror/language';
import { toml } from '@codemirror/legacy-modes/mode/toml';
import { search, searchKeymap, highlightSelectionMatches } from '@codemirror/search';
import { keymap } from '@codemirror/view';

type ConfigSourceEditorProps = {
  value: string;
  onChange: (value: string) => void;
  editorRef?: Ref<ReactCodeMirrorRef>;
  theme: 'light' | 'dark';
  editable: boolean;
  placeholder: string;
  language?: 'yaml' | 'toml' | 'json';
};

export default function ConfigSourceEditor({
  value,
  onChange,
  editorRef,
  theme,
  editable,
  placeholder,
  language = 'yaml',
}: ConfigSourceEditorProps) {
  const languageExtension = useMemo(() => {
    if (language === 'json') return json();
    if (language === 'toml') return StreamLanguage.define(toml);
    return yaml();
  }, [language]);

  const extensions = useMemo(
    () => [languageExtension, search(), highlightSelectionMatches(), keymap.of(searchKeymap)],
    [languageExtension]
  );

  return (
    <CodeMirror
      ref={editorRef}
      value={value}
      onChange={onChange}
      extensions={extensions}
      theme={theme}
      editable={editable}
      placeholder={placeholder}
      height="100%"
      style={{ height: '100%' }}
      basicSetup={{
        lineNumbers: true,
        highlightActiveLineGutter: true,
        highlightActiveLine: true,
        foldGutter: true,
        dropCursor: true,
        allowMultipleSelections: true,
        indentOnInput: true,
        bracketMatching: true,
        closeBrackets: true,
        autocompletion: false,
        rectangularSelection: true,
        crosshairCursor: false,
        highlightSelectionMatches: true,
        closeBracketsKeymap: true,
        searchKeymap: true,
        foldKeymap: true,
        completionKeymap: false,
        lintKeymap: true,
      }}
    />
  );
}
