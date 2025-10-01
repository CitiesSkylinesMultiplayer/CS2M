import {getModule} from "cs2/modding";
import {useLocalization} from "cs2/l10n";

// Props: label, disabled, error, value, onChange, maxLength
export const InputField = (props : any) => {
    const FocusableEditorItem = getModule('game-ui/editor/widgets/item/editor-item.tsx', 'FocusableEditorItem');
    const ErrorLabel = getModule('game-ui/editor/widgets/label/error-label.tsx', 'ErrorLabel');
    const EditorCSS = getModule('game-ui/editor/widgets/item/editor-item.module.scss', 'classes');
    const StringInputField = getModule('game-ui/editor/widgets/fields/string-input-field.tsx', 'StringInputField');

    const { translate } = useLocalization();

    return (
        <FocusableEditorItem disabled={props.disabled}>
            <div className={EditorCSS.row}>
                <div className={EditorCSS.label}>
                    {translate(props.label, props.label)}
                </div>
                <div className={EditorCSS.control}>
                    <StringInputField className={props.error ? EditorCSS.errorBorder : null} value={props.value} onChange={props.onChange} maxLength={props.maxLength ?? 85}>
                    </StringInputField>
                </div>
            </div>
            <ErrorLabel visible={!!props.error} className={EditorCSS.labelRight} displayName={props.error}>
            </ErrorLabel>
        </FocusableEditorItem>
    )
}

export const InputFieldWide = (props : any) => {
    const FocusableEditorItem = getModule('game-ui/editor/widgets/item/editor-item.tsx', 'FocusableEditorItem');
    const ErrorLabel = getModule('game-ui/editor/widgets/label/error-label.tsx', 'ErrorLabel');
    const EditorCSS = getModule('game-ui/editor/widgets/item/editor-item.module.scss', 'classes');
    const StringInputField = getModule('game-ui/editor/widgets/fields/string-input-field.tsx', 'StringInputField');

    const { translate } = useLocalization();

    return (
        <FocusableEditorItem disabled={props.disabled}>
            <div className={EditorCSS.row}>
                <div className={EditorCSS.control}>
                    <StringInputField className={props.error ? EditorCSS.errorBorder : null} value={props.value} onChange={props.onChange} maxLength={props.maxLength ?? 85}>
                    </StringInputField>
                </div>
            </div>
            <ErrorLabel visible={!!props.error} className={EditorCSS.labelRight} displayName={props.error}>
            </ErrorLabel>
        </FocusableEditorItem>
    )
}