import {getModule} from "cs2/modding";
import {AutoNavigationScope, NavigationDirection} from "cs2/input";

export const InputField = (props : any) => {
    const ColumnFieldCSS = getModule('game-ui/menu/components/shared/game-options/field/column-field.module.scss', 'classes');
    const CityNameFieldCSS = getModule('game-ui/menu/components/shared/game-options/city-name-field/city-name-field.module.scss', 'classes');
    const OptionField = getModule('game-ui/menu/widgets/field/field.tsx', 'OptionField');
    const EllipsisTextInput = getModule('game-ui/common/input/text/ellipsis-text-input/ellipsis-text-input.tsx', 'EllipsisTextInput');

    const label = props.label ?? "";

    return (
        <OptionField id={props.id} label={label} theme={ColumnFieldCSS}>
            <div className={CityNameFieldCSS.cityNameField}>
                <AutoNavigationScope direction={NavigationDirection.Horizontal}>
                    <EllipsisTextInput value={props.value} vkTitle={label} maxLength="85" onChange={props.onChange} disabled={props.disabled}>

                    </EllipsisTextInput>
                </AutoNavigationScope>
            </div>
        </OptionField>
    )
}
